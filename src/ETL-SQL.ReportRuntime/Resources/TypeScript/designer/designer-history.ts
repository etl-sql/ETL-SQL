/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Canvas undo, redo, and clipboard operations.
 */

import type { DesignerPage, DesignerState, DesignerVisual } from './designer-context.js';

export interface DesignerHistoryContext {
    readonly curPage: () => DesignerPage;
    readonly findVis: (id: string) => DesignerVisual | null;
    isDirty: boolean;
    readonly renderAll: () => void;
    readonly selectVisual: (id: string | null, selectionOpts?: { toggle?: boolean; multi?: boolean; skipEditorSync?: boolean; skipCanvas?: boolean; }) => void;
    selVisualId: string | null;
    selVisualIds: Set<string>;
    readonly state: DesignerState;
    readonly uid: () => string;
}

export function createDesignerHistory(context: DesignerHistoryContext) {
    let clipboardVisuals: DesignerVisual[] = [];

    const redoStack: string[] = [];

    const undoStack: string[] = [];

    function pushUndoState() {
        if (undoStack.length >= 20) undoStack.shift();
        undoStack.push(JSON.stringify(context.state.pages));
        redoStack.length = 0;
        context.isDirty = true;
    }

    function undoCanvasState() {
        if (!undoStack.length) return;
        redoStack.push(JSON.stringify(context.state.pages));
        context.state.pages = JSON.parse(undoStack.pop() ?? '[]') as DesignerPage[];
        context.renderAll();
    }

    function redoCanvasState() {
        if (!redoStack.length) return;
        undoStack.push(JSON.stringify(context.state.pages));
        context.state.pages = JSON.parse(redoStack.pop() ?? '[]') as DesignerPage[];
        context.renderAll();
    }

    function duplicateVisual(id: string): void {
        const v = context.findVis(id);
        if (!v) return;
        pushUndoState();
        const newId = context.uid();
        const clone = JSON.parse(JSON.stringify(v));
        clone.id = newId;
        clone.name = (clone.type || 'vis').toLowerCase() + '_' + newId.slice(2);
        clone.gridRow = (v.gridRow || 1) + (v.gridRowSpan || 4);
        if (clone.gridRow > 50) clone.gridRow = (v.gridRow || 1) + 1;
        const page = context.curPage();
        if (page?.visuals) page.visuals.push(clone);
        context.selectVisual(newId);
        context.renderAll();
    }

    function copySelectedVisuals() {
        if (context.selVisualIds.size === 0) return;
        clipboardVisuals = Array.from(context.selVisualIds)
            .map(id => context.findVis(id))
            .filter(Boolean)
            .map(v => JSON.parse(JSON.stringify(v)) as DesignerVisual);
    }

    function pasteVisuals() {
        if (!clipboardVisuals.length) return;
        pushUndoState();
        const page = context.curPage();
        if (!page.visuals) page.visuals = [];
        const newSelIds = [];

        for (const orig of clipboardVisuals) {
            const newId = context.uid();
            const clone = JSON.parse(JSON.stringify(orig));
            clone.id = newId;
            clone.name = (clone.type || 'vis').toLowerCase() + '_' + newId.slice(2);
            clone.gridRow = Math.max(1, (clone.gridRow || 1) + 1);
            clone.gridCol = Math.min(12, Math.max(1, (clone.gridCol || 1) + 1));
            page.visuals.push(clone);
            newSelIds.push(newId);
        }

        context.selVisualIds.clear();
        for (const id of newSelIds) context.selVisualIds.add(id);
        context.selVisualId = context.selVisualIds.size === 1 ? Array.from(context.selVisualIds)[0] : null;
        context.renderAll();
    }

    return { pushUndoState, undoCanvasState, redoCanvasState, duplicateVisual, copySelectedVisuals, pasteVisuals };
}
