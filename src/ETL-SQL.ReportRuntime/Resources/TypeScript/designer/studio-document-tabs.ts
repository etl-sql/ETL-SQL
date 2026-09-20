/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Document tab rendering, keyboard navigation, rename, and overflow.
 */

import { _escapeHtml, _feedback, _fileIcon, _studioIcon, asHtml, closestElement, errorMessage, queryElement, queryElements } from './studio-context.js';

import type { StudioDom, StudioDomElement, StudioDynamic, StudioOptions, StudioRuntimeDocument, StudioRuntimeState } from './studio-context.js';

export interface StudioDocumentTabsContext {
    readonly closeDoc: (docId: string) => Promise<void>;
    readonly getActiveDoc: () => StudioRuntimeDocument | null;
    readonly opts: StudioOptions;
    readonly renderSidebarContent: (activity: string) => void;
    readonly scrollLeftBtn: StudioDomElement;
    readonly scrollRightBtn: StudioDomElement;
    readonly shell: StudioDom;
    readonly state: StudioRuntimeState;
    readonly switchDoc: (docId: string) => Promise<void>;
    readonly tabDropdown: StudioDomElement;
    readonly tabOverflowBtn: StudioDomElement;
    readonly tabsContainer: StudioDomElement;
}

export function createStudioDocumentTabs(hostContext: StudioDocumentTabsContext) {

    function renderTabs() {
        const hadFocus = hostContext.tabsContainer ? hostContext.tabsContainer.contains(document.activeElement) : false;
        hostContext.tabsContainer.innerHTML = '';
        hostContext.tabsContainer.setAttribute('role', 'tablist');
        hostContext.tabsContainer.setAttribute('aria-label', 'Open documents');

        // 🏠 Home Tab
        const isHomeActive = hostContext.state.activeDocId === '__home__';
        const homeTab = document.createElement('div');
        homeTab.className = `etlsql-studio-tab ${isHomeActive ? 'active' : ''}`;
        homeTab.setAttribute('role', 'tab');
        homeTab.setAttribute('id', 'etlsql-tab-home');
        homeTab.setAttribute('aria-selected', isHomeActive ? 'true' : 'false');
        homeTab.setAttribute('tabindex', isHomeActive ? '0' : '-1');
        homeTab.setAttribute('aria-label', 'Home');
        homeTab.innerHTML = `
            <span class="etlsql-tab-icon">${_studioIcon('explorer', 14)}</span>
            <span class="etlsql-tab-title">Home</span>
        `;
        homeTab.addEventListener('click', () => hostContext.switchDoc('__home__'));
        homeTab.addEventListener('keydown', handleTabKeydown);
        hostContext.tabsContainer.appendChild(homeTab);

        hostContext.state.documents.forEach(doc => {
            const isDocActive = doc.id === hostContext.state.activeDocId;
            const tab = document.createElement('div');
            tab.className = `etlsql-studio-tab ${isDocActive ? 'active' : ''}`;
            tab.setAttribute('role', 'tab');
            tab.setAttribute('id', `etlsql-tab-${doc.id}`);
            tab.setAttribute('aria-selected', isDocActive ? 'true' : 'false');
            tab.setAttribute('tabindex', isDocActive ? '0' : '-1');
            tab.setAttribute('aria-label', `${doc.name}${doc.isDirty ? ' (unsaved changes)' : ''}`);
            tab.dataset.docId = doc.id;
            tab.innerHTML = `
                <span class="etlsql-tab-icon">${_fileIcon(doc.path)}</span>
                <span class="etlsql-tab-title" title="${_escapeHtml(doc.path)}">${_escapeHtml(doc.name)}</span>
                ${doc.isDirty ? '<span class="etlsql-tab-dirty">●</span>' : ''}
                <button type="button" class="etlsql-tab-close" title="Close Tab" aria-label="Close ${_escapeHtml(doc.name)}" tabindex="-1">${_studioIcon('close', 10)}</button>
            `;

            tab.addEventListener('click', (e) => {
                if (closestElement(e, '.etlsql-tab-rename-input')) {
                    e.stopPropagation();
                    return;
                }
                if (closestElement(e, '.etlsql-tab-close')) {
                    e.stopPropagation();
                    hostContext.closeDoc(doc.id);
                } else if (doc.id !== hostContext.state.activeDocId) {
                    hostContext.switchDoc(doc.id);
                }
            });

            tab.addEventListener('keydown', handleTabKeydown);

            const title = queryElement(tab, '.etlsql-tab-title');
            if (hostContext.opts.onRenameDocument && title) {
                /** @type {HTMLElement} */ (title).title = `Double-click to rename ${doc.path}`;
                title.addEventListener('dblclick', (event) => {
                    event.preventDefault();
                    event.stopPropagation();
                    beginTabRename(tab, doc);
                });
            }

            hostContext.tabsContainer.appendChild(tab);
        });

        const publishBtn = queryElement<HTMLButtonElement>(hostContext.shell, '[data-action="publish"]');
        if (publishBtn && hostContext.opts.onCreateDocument) {
            const doc = hostContext.getActiveDoc();
            const canPublish = Boolean(doc && !doc.reportId);
            publishBtn.disabled = !canPublish;
            publishBtn.style.opacity = canPublish ? '1' : '0.5';
            publishBtn.title = doc?.reportId
                ? `'${doc.name}' is published in the catalog.`
                : 'Publish this draft report to the Portal Catalog';
        }

        requestAnimationFrame(() => {
            const activeTab = queryElement(hostContext.tabsContainer, '.etlsql-studio-tab.active') as HTMLElement | null;
            if (activeTab) {
                activeTab.scrollIntoView({ behavior: 'smooth', inline: 'nearest', block: 'nearest' });
                if (hadFocus) {
                    activeTab.focus();
                }
            }
            updateTabOverflowState();
        });
    }

    function handleTabKeydown(e: KeyboardEvent) {
        if (closestElement(e, '.etlsql-tab-rename-input')) return;
        const tabs = Array.from(hostContext.tabsContainer ? queryElements(hostContext.tabsContainer, '.etlsql-studio-tab') : []).map(asHtml);
        const currentTarget = asHtml(e.currentTarget);
        const currentIdx = tabs.indexOf(currentTarget);
        if (currentIdx === -1) return;

        if (e.key === 'ArrowRight' || e.key === 'ArrowDown') {
            e.preventDefault();
            const nextTab = asHtml(tabs[(currentIdx + 1) % tabs.length]);
            const docId = nextTab?.dataset.docId || '__home__';
            hostContext.switchDoc(docId);
            nextTab?.focus();
        } else if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') {
            e.preventDefault();
            const prevTab = asHtml(tabs[(currentIdx - 1 + tabs.length) % tabs.length]);
            const docId = prevTab?.dataset.docId || '__home__';
            hostContext.switchDoc(docId);
            prevTab?.focus();
        } else if (e.key === 'Home') {
            e.preventDefault();
            const firstTab = asHtml(tabs[0]);
            const docId = firstTab?.dataset.docId || '__home__';
            hostContext.switchDoc(docId);
            firstTab?.focus();
        } else if (e.key === 'End') {
            e.preventDefault();
            const lastTab = asHtml(tabs[tabs.length - 1]);
            const docId = lastTab?.dataset.docId || '__home__';
            hostContext.switchDoc(docId);
            lastTab?.focus();
        } else if (e.key === 'Delete' || ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'w')) {
            e.preventDefault();
            const docId = currentTarget.dataset.docId;
            if (docId && docId !== '__home__') {
                hostContext.closeDoc(docId);
            }
        } else if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            currentTarget.click();
        }
    }

    function beginTabRename(tab: HTMLElement, doc: StudioRuntimeDocument) {
        if (!hostContext.opts.onRenameDocument || queryElement(tab, '.etlsql-tab-rename-input')) return;

        const title = queryElement(tab, '.etlsql-tab-title');
        if (!title) return;
        const input = document.createElement('input');
        input.type = 'text';
        input.className = 'etlsql-tab-rename-input';
        input.value = doc.name || '';
        input.setAttribute('aria-label', `Rename ${doc.name}`);
        input.spellcheck = false;
        title.replaceWith(input);

        let settled = false;
        const finish = async (commit: boolean) => {
            if (settled) return;
            settled = true;
            const requestedName = input.value.trim();
            if (!commit || !requestedName || requestedName === doc.name) {
                renderTabs();
                return;
            }

            input.disabled = true;
            const oldPath = doc.path;
            try {
                const renamed = await hostContext.opts.onRenameDocument?.(doc, requestedName);
                if (!renamed?.path) throw new Error('The host did not return the renamed file path.');
                doc.path = renamed.path;
                doc.name = renamed.name || renamed.path.split('/').pop()?.split('\\').pop() || requestedName;
                const workspaceFile = hostContext.state.workspaceFiles.find((file: StudioDynamic) => file.path === oldPath);
                if (workspaceFile) workspaceFile.path = doc.path;
                renderTabs();
                hostContext.renderSidebarContent(hostContext.state.activeActivity);
                _feedback.notify(`Renamed ${oldPath} to ${doc.path}`, { title: 'File Renamed', tone: 'success' });
            } catch (error) {
                renderTabs();
                _feedback.notify(errorMessage(error) || 'The file could not be renamed.', { title: 'Rename Failed', tone: 'error' });
            }
        };

        input.addEventListener('click', event => event.stopPropagation());
        input.addEventListener('dblclick', event => event.stopPropagation());
        input.addEventListener('keydown', event => {
            if (event.key === 'Enter') {
                event.preventDefault();
                void finish(true);
            } else if (event.key === 'Escape') {
                event.preventDefault();
                void finish(false);
            }
        });
        input.addEventListener('blur', () => void finish(true));
        input.focus();
        const extensionIndex = input.value.lastIndexOf('.');
        input.setSelectionRange(0, extensionIndex > 0 ? extensionIndex : input.value.length);
    }

    function updateTabOverflowState() {
        if (!hostContext.tabsContainer) return;
        const hasOverflow = hostContext.tabsContainer.scrollWidth > hostContext.tabsContainer.clientWidth + 2;
        const scrollLeft = hostContext.tabsContainer.scrollLeft;
        const maxScroll = hostContext.tabsContainer.scrollWidth - hostContext.tabsContainer.clientWidth;

        if (hostContext.scrollLeftBtn) {
            hostContext.scrollLeftBtn.style.display = hasOverflow ? 'inline-flex' : 'none';
            hostContext.scrollLeftBtn.disabled = scrollLeft <= 2;
        }
        if (hostContext.scrollRightBtn) {
            hostContext.scrollRightBtn.style.display = hasOverflow ? 'inline-flex' : 'none';
            hostContext.scrollRightBtn.disabled = scrollLeft >= maxScroll - 2;
        }
        if (hostContext.tabOverflowBtn) {
            hostContext.tabOverflowBtn.style.display = (hasOverflow || hostContext.state.documents.length > 2) ? 'inline-flex' : 'none';
        }
    }

    function toggleTabDropdown(show?: boolean) {
        if (!hostContext.tabDropdown) return;
        const willShow = typeof show === 'boolean' ? show : hostContext.tabDropdown.hidden;
        if (!willShow) {
            hostContext.tabDropdown.hidden = true;
            hostContext.tabOverflowBtn?.classList.remove('active');
            return;
        }

        renderTabDropdown();
        hostContext.tabDropdown.hidden = false;
        hostContext.tabOverflowBtn?.classList.add('active');
    }

    function renderTabDropdown() {
        if (!hostContext.tabDropdown) return;
        hostContext.tabDropdown.innerHTML = '';

        const homeItem = document.createElement('button');
        homeItem.type = 'button';
        homeItem.className = `etlsql-studio-tab-dropdown-item ${hostContext.state.activeDocId === '__home__' ? 'active' : ''}`;
        homeItem.innerHTML = `
            <span>${_studioIcon('explorer', 14)}</span>
            <span class="etlsql-studio-tab-dropdown-title">Home</span>
            ${hostContext.state.activeDocId === '__home__' ? '<span style="font-size:11px;">✓</span>' : ''}
        `;
        homeItem.addEventListener('click', () => {
            hostContext.switchDoc('__home__');
            toggleTabDropdown(false);
        });
        hostContext.tabDropdown.appendChild(homeItem);

        hostContext.state.documents.forEach(doc => {
            const item = document.createElement('div');
            item.className = `etlsql-studio-tab-dropdown-item ${doc.id === hostContext.state.activeDocId ? 'active' : ''}`;
            item.innerHTML = `
                <span>${_fileIcon(doc.path)}</span>
                <span class="etlsql-studio-tab-dropdown-title" title="${_escapeHtml(doc.path)}">${_escapeHtml(doc.name)}</span>
                ${doc.isDirty ? '<span class="etlsql-tab-dirty">●</span>' : ''}
                <button type="button" class="etlsql-studio-tab-dropdown-close" title="Close Tab">${_studioIcon('close', 10)}</button>
            `;
            item.addEventListener('click', (e) => {
                if (closestElement(e, '.etlsql-studio-tab-dropdown-close')) {
                    e.stopPropagation();
                    hostContext.closeDoc(doc.id);
                    renderTabDropdown();
                } else {
                    hostContext.switchDoc(doc.id);
                    toggleTabDropdown(false);
                }
            });
            hostContext.tabDropdown.appendChild(item);
        });
    }

    return { renderTabs, updateTabOverflowState, toggleTabDropdown };
}
