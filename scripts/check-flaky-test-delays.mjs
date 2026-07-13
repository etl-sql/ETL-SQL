#!/usr/bin/env node
// Guardrail against the "sleep-then-assert" flaky-test anti-pattern (v0.15.0 debt).
//
// Flags `await Task.Delay(<literal ms>)` in test files that is the SOLE synchronization before a
// POSITIVE assertion (asserting an async action *did* happen) — the shape that flaked in CI
// (SchedulerServiceTests, LinterTests). Legitimate uses are excluded automatically:
//   - inside a polling loop (while/for/foreach/do, or a deadline/Stopwatch guard)
//   - a `Task.WhenAny(..., Task.Delay(...))` timeout sentinel
//   - a delay followed by a *negative*/absence assertion (Empty/Null/False/Never/DoesNotContain/Throws)
//   - `Task.Delay(Timeout.Infinite, ...)` background placeholders, or non-literal delays
//
// A site that is genuinely fine but still matches can be annotated on the delay line with a
// trailing `// flaky-delay-ok: <reason>` comment; the check then skips it (and requires a reason).
//
// Reference fix patterns: SchedulerServiceTests.WaitUntilAsync, LinterTests ConcurrencyTracker.
//
// Usage: node scripts/check-flaky-test-delays.mjs   (exit 1 if any un-annotated candidate is found)

import { readFile } from 'node:fs/promises';
import { glob } from 'node:fs/promises';

const POSITIVE_ASSERT = /(Assert\.(NotNull|True|Equal|Contains|Single|NotEmpty|IsType|Same)\b|\.Verify\([^)]*\b(AtLeastOnce|Times\.Once|Times\.Exactly|Times\.AtLeast)\b)/;
const NEGATIVE_ASSERT = /(Assert\.(Empty|Null|False|DoesNotContain|ThrowsAsync|Throws)\b|Times\.Never)/;
const LOOP_HINT = /\b(while|for|foreach|do)\s*[\(\{]|\b(deadline|Stopwatch|ElapsedMilliseconds|TickCount|DateTime(Offset)?\.(Now|UtcNow))\b/;
const DELAY = /await\s+Task\.Delay\(\s*\d/; // literal-ms delay only

const files = [];
for await (const f of glob('tests/**/*.cs')) files.push(f);

const findings = [];
for (const file of files.sort()) {
  const lines = (await readFile(file, 'utf8')).split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    if (!DELAY.test(line)) continue;
    if (line.includes('WhenAny') || line.includes('Timeout.Infinite')) continue;
    if (/\/\/\s*flaky-delay-ok:\s*\S/.test(line)) continue; // reviewed + reason given

    // In a polling loop / deadline guard? A poll loop is `while/for(...){ ... if(cond) break/return;
    // await Task.Delay(N); }` — the delay is the last statement before the loop's closing brace, and
    // the assertion comes AFTER the loop. Detect via: a loop header within ~15 lines back; a
    // loop-exit (break/continue/return) just before the delay; or a `}` right after it.
    const back = lines.slice(Math.max(0, i - 15), i).join('\n');
    const nearBack = lines.slice(Math.max(0, i - 4), i).join('\n');
    const closesLoop = lines.slice(i + 1, i + 3).some((l) => l.trim() === '}');
    if (LOOP_HINT.test(back) || LOOP_HINT.test(line) || /\b(break|continue|return)\b/.test(nearBack) || closesLoop) continue;

    // Followed by a positive assertion (and not a negative one) within a few statements?
    const ahead = lines.slice(i + 1, i + 6);
    const aheadText = ahead.join('\n');
    if (POSITIVE_ASSERT.test(aheadText) && !NEGATIVE_ASSERT.test(aheadText.split('\n').slice(0, 3).join('\n'))) {
      findings.push({ file, line: i + 1, text: line.trim() });
    }
  }
}

if (findings.length === 0) {
  console.log('OK: no sleep-then-assert flaky-test candidates found.');
  process.exit(0);
}

console.error(`Found ${findings.length} sleep-then-assert candidate(s). Convert to poll-for-condition`);
console.error('(see SchedulerServiceTests.WaitUntilAsync), or annotate the delay line with');
console.error('`// flaky-delay-ok: <reason>` if it is genuinely safe.\n');
for (const f of findings) console.error(`  ${f.file}:${f.line}  ${f.text}`);
process.exit(1);
