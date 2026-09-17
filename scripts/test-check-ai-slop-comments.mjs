// Unit tests for the AI slop comment checker guardrail
import { checkCommentLine } from './check-ai-slop-comments.mjs';

function assert(condition, message) {
  if (!condition) throw new Error(`FAIL: ${message}`);
}

// 1. Corporate buzzwords & clichés
assert(checkCommentLine('// This seamlessly integrates with the database'), 'catches seamlessly');
assert(checkCommentLine('// We leverage this component here'), 'catches leverage');
assert(checkCommentLine("/* In today's digital landscape */"), 'catches structural cliché');

// 2. Procedural steps
assert(checkCommentLine('// Step 1: Initialize variables'), 'catches step marker');
assert(checkCommentLine('// Step 2 - Parse response'), 'catches step hyphen');
assert(checkCommentLine('// First, we parse the tokens'), 'catches first we narrative');

// 3. Conversational commentary
assert(checkCommentLine('// Added to fix bug in query generation'), 'catches added to fix');
assert(checkCommentLine('// Handle edge case for missing id'), 'catches handle edge case');
assert(checkCommentLine('// Need to make sure the stream is open'), 'catches need to make sure');

// 4. Trivial restatements
assert(checkCommentLine('// Loop through items'), 'catches loop through items');
assert(checkCommentLine('// Iterate over tokens.'), 'catches iterate over tokens');
assert(checkCommentLine('// Return the result;'), 'catches return the result');
assert(checkCommentLine('// Check if user is null'), 'catches null check restatement');
assert(checkCommentLine('// Create a new instance'), 'catches instantiation restatement');
assert(checkCommentLine('// Increment count by 1'), 'catches increment restatement');

// 5. Allowed technical comments
assert(!checkCommentLine('// Consolidate into a single DataTable for the variable.'), 'allows substantive domain comments');
assert(!checkCommentLine("// Check if it's a 1=0 special clause"), 'allows domain-specific condition descriptions');
assert(!checkCommentLine('// Sources and columns are pairs from expressions; process together.'), 'allows architecture comments');

// 6. Escape hatches
assert(!checkCommentLine('// Step 1: Setup scenario // comment-ok: explicit test stage marker'), 'escape hatch on same line');
assert(!checkCommentLine('// Check if user is null', '// comment-ok: intentional null sentinel guard'), 'escape hatch on previous line');
assert(!checkCommentLine('// Loop through items // slop-ok: verified performance loop'), 'slop-ok escape hatch');

console.log('check-ai-slop-comments tests passed');
