/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-git-diff.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/**
 * Aligns two script revisions for Studio's side-by-side Git viewer.
 * The bounded LCS keeps ordinary scripts readable without allowing a very large file to stall the UI.
 */
export function buildSideBySideDiff(baselineContent, workingContent) {
    const left = String(baselineContent ?? '').replace(/\r\n?/g, '\n').split('\n');
    const right = String(workingContent ?? '').replace(/\r\n?/g, '\n').split('\n');
    const operations = left.length * right.length <= 400000
        ? lcsOperations(left, right)
        : positionalOperations(left, right);
    return alignChangeRuns(operations);
}

function lcsOperations(left, right) {
    const widths = right.length + 1;
    const table = new Uint32Array((left.length + 1) * widths);
    for (let leftIndex = left.length - 1; leftIndex >= 0; leftIndex--) {
        for (let rightIndex = right.length - 1; rightIndex >= 0; rightIndex--) {
            const offset = leftIndex * widths + rightIndex;
            table[offset] = left[leftIndex] === right[rightIndex]
                ? table[(leftIndex + 1) * widths + rightIndex + 1] + 1
                : Math.max(table[(leftIndex + 1) * widths + rightIndex], table[offset + 1]);
        }
    }

    const operations = [];
    let leftIndex = 0;
    let rightIndex = 0;
    while (leftIndex < left.length || rightIndex < right.length) {
        if (leftIndex < left.length && rightIndex < right.length && left[leftIndex] === right[rightIndex]) {
            operations.push({ kind: 'equal', text: left[leftIndex] });
            leftIndex++;
            rightIndex++;
        } else if (rightIndex >= right.length || (leftIndex < left.length
            && table[(leftIndex + 1) * widths + rightIndex] >= table[leftIndex * widths + rightIndex + 1])) {
            operations.push({ kind: 'delete', text: left[leftIndex++] });
        } else {
            operations.push({ kind: 'add', text: right[rightIndex++] });
        }
    }
    return operations;
}

function positionalOperations(left, right) {
    const operations = [];
    for (let index = 0; index < Math.max(left.length, right.length); index++) {
        if (left[index] === right[index]) operations.push({ kind: 'equal', text: left[index] });
        else {
            if (index < left.length) operations.push({ kind: 'delete', text: left[index] });
            if (index < right.length) operations.push({ kind: 'add', text: right[index] });
        }
    }
    return operations;
}

function alignChangeRuns(operations) {
    const rows = [];
    let leftNumber = 1;
    let rightNumber = 1;
    for (let index = 0; index < operations.length;) {
        const operation = operations[index];
        if (operation.kind === 'equal') {
            rows.push({ kind: 'equal', leftNumber: leftNumber++, rightNumber: rightNumber++, leftText: operation.text, rightText: operation.text });
            index++;
            continue;
        }

        const deleted = [];
        const added = [];
        while (index < operations.length && operations[index].kind !== 'equal') {
            const change = operations[index++];
            (change.kind === 'delete' ? deleted : added).push(change.text);
        }
        for (let changeIndex = 0; changeIndex < Math.max(deleted.length, added.length); changeIndex++) {
            const hasLeft = changeIndex < deleted.length;
            const hasRight = changeIndex < added.length;
            rows.push({
                kind: hasLeft && hasRight ? 'change' : hasLeft ? 'delete' : 'add',
                leftNumber: hasLeft ? leftNumber++ : null,
                rightNumber: hasRight ? rightNumber++ : null,
                leftText: hasLeft ? deleted[changeIndex] : '',
                rightText: hasRight ? added[changeIndex] : '',
            });
        }
    }
    return rows;
}
