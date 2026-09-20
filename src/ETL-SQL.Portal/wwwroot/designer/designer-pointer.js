// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer-pointer.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/designer-pointer.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Canvas marquee, drag, and resize gestures.
 */
import { datasetValue, queryElement, queryElements } from './designer-context.js';
export function createDesignerPointer(context) {
    let ghostEl = null;
    function handleMarqueeMove(e) {
        if (!context.isMarquee || !context.marqueeEl)
            return;
        const wrapRect = context.canvasWrap.getBoundingClientRect();
        const curX = e.clientX;
        const curY = e.clientY;
        const left = Math.min(context.marqueeStartX, curX) - wrapRect.left + context.canvasWrap.scrollLeft;
        const top = Math.min(context.marqueeStartY, curY) - wrapRect.top + context.canvasWrap.scrollTop;
        const width = Math.abs(curX - context.marqueeStartX);
        const height = Math.abs(curY - context.marqueeStartY);
        context.marqueeEl.style.left = `${left}px`;
        context.marqueeEl.style.top = `${top}px`;
        context.marqueeEl.style.width = `${width}px`;
        context.marqueeEl.style.height = `${height}px`;
        const mRect = context.marqueeEl.getBoundingClientRect();
        for (const card of queryElements(context.canvasGrid, '.etlsql-dsgn-visual-card')) {
            const cRect = card.getBoundingClientRect();
            const intersects = !(mRect.right < cRect.left || mRect.left > cRect.right || mRect.bottom < cRect.top || mRect.top > cRect.bottom);
            if (intersects) {
                context.selVisualIds.add(datasetValue(card, 'vid'));
            }
            else if (!e.shiftKey && !e.ctrlKey && !e.metaKey) {
                context.selVisualIds.delete(datasetValue(card, 'vid'));
            }
        }
        context.selVisualId = context.selVisualIds.size === 1 ? Array.from(context.selVisualIds)[0] : null;
        for (const card of queryElements(context.canvasGrid, '.etlsql-dsgn-visual-card')) {
            card.classList.toggle('selected', context.selVisualIds.has(datasetValue(card, 'vid')));
        }
        context.renderAlignmentToolbar();
    }
    function handleMarqueeUp() {
        if (context.marqueeEl) {
            context.marqueeEl.style.display = 'none';
        }
        context.isMarquee = false;
        document.removeEventListener('mousemove', handleMarqueeMove);
        document.removeEventListener('mouseup', handleMarqueeUp);
        context.renderTree();
        context.renderProps();
    }
    function handleMouseMove(e) {
        if (!context.activeId || !context.activeCardEl)
            return;
        const v = context.findVis(context.activeId);
        if (!v)
            return;
        const gridRect = context.canvasGrid.getBoundingClientRect();
        const gridW = gridRect.width - 32;
        const W_col = (gridW - 11 * 6) / 12;
        const dragRect = context.initialRect;
        if (context.isDragging) {
            if (!dragRect)
                return;
            if (!ghostEl) {
                ghostEl = document.createElement('div');
                ghostEl.className = 'etlsql-dsgn-grid-ghost';
                ghostEl.style.gridColumn = `${context.startCol} / span ${context.startColSpan}`;
                ghostEl.style.gridRow = `${context.startRow} / span ${context.startRowSpan}`;
                context.canvasGrid.appendChild(ghostEl);
                context.activeCardEl.classList.add('dragging');
                context.activeCardEl.style.width = `${dragRect.width}px`;
                context.activeCardEl.style.height = `${dragRect.height}px`;
                context.activeCardEl.style.left = `${dragRect.left - gridRect.left}px`;
                context.activeCardEl.style.top = `${dragRect.top - gridRect.top}px`;
            }
            const dx = e.clientX - context.startX;
            const dy = e.clientY - context.startY;
            context.activeCardEl.style.transform = `translate3d(${dx}px, ${dy}px, 0)`;
            const currentLeft = (dragRect.left - gridRect.left) + dx - 16;
            const currentTop = (dragRect.top - gridRect.top) + dy - 16;
            let newCol = Math.round(currentLeft / (W_col + 6)) + 1;
            newCol = Math.max(1, Math.min(12, newCol));
            let newColSpan = context.startColSpan;
            if (newCol + newColSpan - 1 > 12) {
                newColSpan = Math.max(1, 13 - newCol);
            }
            let newRow = Math.round(currentTop / 66) + 1;
            newRow = Math.max(1, newRow);
            context.targetCol = newCol;
            context.targetRow = newRow;
            context.targetColSpan = newColSpan;
            context.targetRowSpan = context.startRowSpan;
            ghostEl.style.gridColumn = `${newCol} / span ${newColSpan}`;
            ghostEl.style.gridRow = `${newRow} / span ${context.startRowSpan}`;
            // Highlight hover container drop zones
            let hoverContainerId = null;
            if (v.type !== 'CONTAINER') {
                const containers = context.curVis().filter(c => c.type === 'CONTAINER');
                const parentContainer = containers.find(c => {
                    const cColStart = c.gridCol || 1;
                    const cColEnd = cColStart + (c.gridColSpan || 12) - 1;
                    const cRowStart = c.gridRow || 1;
                    const cRowEnd = cRowStart + (c.gridRowSpan || 4) - 1;
                    return context.targetCol >= cColStart && context.targetCol <= cColEnd && context.targetRow >= cRowStart && context.targetRow <= cRowEnd;
                });
                if (parentContainer)
                    hoverContainerId = parentContainer.id;
            }
            for (const card of queryElements(context.canvasGrid, '.etlsql-dsgn-visual-card.is-container')) {
                if (datasetValue(card, 'vid') === hoverContainerId) {
                    card.classList.add('drop-zone-hover');
                }
                else {
                    card.classList.remove('drop-zone-hover');
                }
            }
        }
        else if (context.isResizing) {
            if (!ghostEl) {
                ghostEl = document.createElement('div');
                ghostEl.className = 'etlsql-dsgn-grid-ghost';
                ghostEl.style.gridColumn = `${context.startCol} / span ${context.startColSpan}`;
                ghostEl.style.gridRow = `${context.startRow} / span ${context.startRowSpan}`;
                context.canvasGrid.appendChild(ghostEl);
            }
            const cardRightX = e.clientX - gridRect.left - 16;
            const cardBottomY = e.clientY - gridRect.top - 16;
            const cardLeftX = (context.startCol - 1) * (W_col + 6);
            const cardTopY = (context.startRow - 1) * 66;
            let newColSpan = Math.round((cardRightX - cardLeftX + 6) / (W_col + 6));
            newColSpan = Math.max(1, Math.min(13 - context.startCol, newColSpan));
            let newRowSpan = Math.round((cardBottomY - cardTopY + 6) / 66);
            newRowSpan = Math.max(1, newRowSpan);
            context.targetCol = context.startCol;
            context.targetRow = context.startRow;
            context.targetColSpan = newColSpan;
            context.targetRowSpan = newRowSpan;
            context.activeCardEl.style.gridColumn = `${context.startCol} / span ${newColSpan}`;
            context.activeCardEl.style.gridRow = `${context.startRow} / span ${newRowSpan}`;
            ghostEl.style.gridColumn = `${context.startCol} / span ${newColSpan}`;
            ghostEl.style.gridRow = `${context.startRow} / span ${newRowSpan}`;
        }
        // Draw grid snapping guides
        let showVGuide = false;
        let showHGuide = false;
        let vGuideCol = 1;
        let hGuideRow = 1;
        if (context.isDragging || context.isResizing) {
            const otherVis = context.curVis().filter(other => other.id !== context.activeId);
            for (const other of otherVis) {
                const otherColStart = other.gridCol || 1;
                const otherColEnd = otherColStart + (other.gridColSpan || 12);
                const otherRowStart = other.gridRow || 1;
                const otherRowEnd = otherRowStart + (other.gridRowSpan || 4);
                const targetColStart = context.targetCol;
                const targetColEnd = context.targetCol + context.targetColSpan;
                const targetRowStart = context.targetRow;
                const targetRowEnd = context.targetRow + context.targetRowSpan;
                if (targetColStart === otherColStart) {
                    showVGuide = true;
                    vGuideCol = targetColStart;
                }
                else if (targetColEnd === otherColEnd) {
                    showVGuide = true;
                    vGuideCol = targetColEnd;
                }
                else if (targetColStart === otherColEnd) {
                    showVGuide = true;
                    vGuideCol = targetColStart;
                }
                else if (targetColEnd === otherColStart) {
                    showVGuide = true;
                    vGuideCol = targetColEnd;
                }
                if (targetRowStart === otherRowStart) {
                    showHGuide = true;
                    hGuideRow = targetRowStart;
                }
                else if (targetRowEnd === otherRowEnd) {
                    showHGuide = true;
                    hGuideRow = targetRowEnd;
                }
                else if (targetRowStart === otherRowEnd) {
                    showHGuide = true;
                    hGuideRow = targetRowStart;
                }
                else if (targetRowEnd === otherRowStart) {
                    showHGuide = true;
                    hGuideRow = targetRowEnd;
                }
            }
        }
        let vGuideEl = queryElement(context.canvasGrid, '.etlsql-dsgn-guide-v');
        if (showVGuide) {
            if (!vGuideEl) {
                vGuideEl = document.createElement('div');
                vGuideEl.className = 'etlsql-dsgn-guide-v';
                context.canvasGrid.appendChild(vGuideEl);
            }
            /** @type {HTMLElement} */ (vGuideEl).style.gridColumnStart = `${vGuideCol}`;
            /** @type {HTMLElement} */ (vGuideEl).style.display = 'block';
        }
        else if (vGuideEl) {
            /** @type {HTMLElement} */ (vGuideEl).style.display = 'none';
        }
        let hGuideEl = queryElement(context.canvasGrid, '.etlsql-dsgn-guide-h');
        if (showHGuide) {
            if (!hGuideEl) {
                hGuideEl = document.createElement('div');
                hGuideEl.className = 'etlsql-dsgn-guide-h';
                context.canvasGrid.appendChild(hGuideEl);
            }
            /** @type {HTMLElement} */ (hGuideEl).style.gridRowStart = `${hGuideRow}`;
            /** @type {HTMLElement} */ (hGuideEl).style.display = 'block';
        }
        else if (hGuideEl) {
            /** @type {HTMLElement} */ (hGuideEl).style.display = 'none';
        }
    }
    function handleMouseUp() {
        if (ghostEl) {
            ghostEl.remove();
            ghostEl = null;
        }
        for (const card of queryElements(context.canvasGrid, '.etlsql-dsgn-visual-card.is-container')) {
            card.classList.remove('drop-zone-hover');
        }
        const vGuide = queryElement(context.canvasGrid, '.etlsql-dsgn-guide-v');
        if (vGuide)
            vGuide.remove();
        const hGuide = queryElement(context.canvasGrid, '.etlsql-dsgn-guide-h');
        if (hGuide)
            hGuide.remove();
        if (context.activeId && context.activeCardEl) {
            context.activeCardEl.classList.remove('dragging');
            context.activeCardEl.style.position = '';
            context.activeCardEl.style.width = '';
            context.activeCardEl.style.height = '';
            context.activeCardEl.style.left = '';
            context.activeCardEl.style.top = '';
            context.activeCardEl.style.transform = '';
            context.activeCardEl.style.zIndex = '';
            context.activeCardEl.style.opacity = '';
            const v = context.findVis(context.activeId);
            if (v) {
                const deltaCol = context.targetCol - (v.gridCol || 1);
                const deltaRow = context.targetRow - (v.gridRow || 1);
                v.gridCol = context.targetCol;
                v.gridRow = context.targetRow;
                v.gridColSpan = context.targetColSpan;
                v.gridRowSpan = context.targetRowSpan;
                if (context.selVisualIds.has(v.id) && context.selVisualIds.size > 1 && context.isDragging && (deltaCol !== 0 || deltaRow !== 0)) {
                    for (const otherId of context.selVisualIds) {
                        if (otherId !== v.id) {
                            const other = context.findVis(otherId);
                            if (other) {
                                other.gridCol = Math.max(1, (other.gridCol || 1) + deltaCol);
                                other.gridRow = Math.max(1, (other.gridRow || 1) + deltaRow);
                            }
                        }
                    }
                }
                else if (v.type === 'CONTAINER' && context.isDragging && (deltaCol !== 0 || deltaRow !== 0)) {
                    for (const child of context.curVis()) {
                        if (child.containerId === v.id) {
                            child.gridCol = Math.max(1, (child.gridCol || 1) + deltaCol);
                            child.gridRow = Math.max(1, (child.gridRow || 1) + deltaRow);
                        }
                    }
                }
                else if (v.type !== 'CONTAINER' && context.isDragging) {
                    const containers = context.curVis().filter(c => c.type === 'CONTAINER' && c.id !== v.id);
                    const parentContainer = containers.find(c => {
                        const cColStart = c.gridCol || 1;
                        const cColEnd = cColStart + (c.gridColSpan || 12) - 1;
                        const cRowStart = c.gridRow || 1;
                        const cRowEnd = cRowStart + (c.gridRowSpan || 4) - 1;
                        return context.targetCol >= cColStart && context.targetCol <= cColEnd && context.targetRow >= cRowStart && context.targetRow <= cRowEnd;
                    });
                    v.containerId = parentContainer ? parentContainer.id : null;
                }
            }
            context.renderCanvas();
            context.renderProps();
            context.syncScriptFromGridDebounced();
        }
        context.isDragging = false;
        context.isResizing = false;
        context.activeId = null;
        context.activeCardEl = null;
        context.initialRect = null;
        document.removeEventListener('mousemove', handleMouseMove);
        document.removeEventListener('mouseup', handleMouseUp);
    }
    return { handleMarqueeMove, handleMarqueeUp, handleMouseMove, handleMouseUp };
}
