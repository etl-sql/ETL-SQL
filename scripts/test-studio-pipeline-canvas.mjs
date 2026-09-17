import assert from 'node:assert/strict';
import {
    PIPELINE_TASK_GROUPS,
    PIPELINE_TASK_KINDS,
    PIPELINE_EDGE_CONDITIONS,
    PALETTE_DRAG_TYPE,
    taskKind,
    taskKindLabel,
    isContainerKind,
    needsALoop,
    attachPipelineTaskEditing,
} from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-pipeline-canvas.js';

// ── 1. Palette groups & kinds inventory ──────────────────────────────────────
assert.ok(Array.isArray(PIPELINE_TASK_GROUPS));
assert.equal(PIPELINE_TASK_GROUPS.length, 4);

const groupIds = PIPELINE_TASK_GROUPS.map(g => g.id);
assert.deepEqual(groupIds, ['work', 'flow', 'files', 'directories']);

for (const group of PIPELINE_TASK_GROUPS) {
    assert.ok(group.id, 'Group has id');
    assert.ok(group.label, 'Group has label');
    assert.ok(group.hint, 'Group has hint');
    assert.ok(Array.isArray(group.kinds) && group.kinds.length > 0, 'Group has kinds');
}

assert.ok(Array.isArray(PIPELINE_TASK_KINDS));
assert.equal(PIPELINE_TASK_KINDS.length, 23);

for (const chip of PIPELINE_TASK_KINDS) {
    assert.ok(chip.id, 'Chip has id');
    assert.ok(chip.label, 'Chip has label');
    assert.ok(chip.glyph, 'Chip has glyph');
    assert.ok(chip.hint, 'Chip has hint');
}

// ── 2. taskKind lookup & case-insensitivity ──────────────────────────────────
const execKind = taskKind('execution');
assert.ok(execKind);
assert.equal(execKind.id, 'execution');
assert.equal(execKind.label, 'Execution');

const execUpper = taskKind('EXECUTION');
assert.ok(execUpper);
assert.equal(execUpper.id, 'execution');

assert.equal(taskKind('non_existent_kind'), null);
assert.equal(taskKind(null), null);
assert.equal(taskKind(''), null);

// ── 3. Container kind predicates ────────────────────────────────────────────
assert.equal(isContainerKind('if'), true);
assert.equal(isContainerKind('IF'), true);
assert.equal(isContainerKind('foreach'), true);
assert.equal(isContainerKind('for'), true);
assert.equal(isContainerKind('while'), true);
assert.equal(isContainerKind('parallel'), true);
assert.equal(isContainerKind('transaction'), true);

assert.equal(isContainerKind('execution'), false);
assert.equal(isContainerKind('validation'), false);
assert.equal(isContainerKind('copyfile'), false);
assert.equal(isContainerKind('unknown'), false);

// ── 4. Loop-required predicates ─────────────────────────────────────────────
assert.equal(needsALoop('break'), true);
assert.equal(needsALoop('BREAK'), true);
assert.equal(needsALoop('continue'), true);
assert.equal(needsALoop('if'), false);
assert.equal(needsALoop('execution'), false);

// ── 5. taskKindLabel helper ──────────────────────────────────────────────────
assert.equal(taskKindLabel('execution'), 'Execution');
assert.equal(taskKindLabel('copyfile'), 'Copy file');
assert.equal(taskKindLabel('parallel'), 'Parallel');
assert.equal(taskKindLabel('non_existent'), 'Task');
assert.equal(taskKindLabel(null), 'Task');

// ── 6. Edge conditions inventory ────────────────────────────────────────────
assert.ok(Array.isArray(PIPELINE_EDGE_CONDITIONS));
assert.equal(PIPELINE_EDGE_CONDITIONS.length, 5);

const conditionIds = PIPELINE_EDGE_CONDITIONS.map(c => c.id);
assert.deepEqual(conditionIds, ['always', 'onsuccess', 'onfailure', 'oncompletion', 'expression']);

for (const condition of PIPELINE_EDGE_CONDITIONS) {
    assert.ok(condition.id, 'Condition has id');
    assert.ok(condition.label, 'Condition has label');
    assert.ok(condition.summary, 'Condition has summary');
    assert.ok(condition.hint, 'Condition has hint');
}

// ── 7. Drag format constant ─────────────────────────────────────────────────
assert.equal(PALETTE_DRAG_TYPE, 'application/x-etlsql-task-kind');

// ── 8. attachPipelineTaskEditing export ──────────────────────────────────────
assert.equal(typeof attachPipelineTaskEditing, 'function');
assert.ok(attachPipelineTaskEditing.length >= 2);

console.log('OK test-studio-pipeline-canvas.mjs: all checks passed.');
