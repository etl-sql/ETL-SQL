// Folder helpers for the Admin "Publish Report" form.
//
// Why this module exists: the publish form's destination-folder dropdown used to read a cached
// folder list, so a folder created moments earlier only appeared after a full page reload, and
// there was no way to create a folder without leaving the publish form. These helpers always fetch
// a fresh folder tree, flatten it (so nested folders are selectable too), and support creating a
// folder inline. Extracted from admin.html so the behavior is unit-testable.

type FolderEntry = Pick<FolderDto, 'id' | 'name'> & Partial<Pick<FolderDto, 'path'>> & {
    children?: readonly FolderEntry[] | null;
};
type EscapeHtml = (value: unknown) => string;
interface FolderClient {
    list(): Promise<readonly FolderEntry[]>;
    create(name: string, parentId: number | null): Promise<FolderEntry>;
}

/** Depth-first flatten of the folder tree (GET /api/folders returns roots with nested children). */
export function flattenFolders(folders: readonly FolderEntry[] | null | undefined, out: FolderEntry[] = []) {
    for (const f of folders || []) {
        out.push(f);
        if (f.children && f.children.length) flattenFolders(f.children, out);
    }
    return out;
}

/** Build <option> markup for a flat folder list. `esc` is the caller's HTML escaper. */
export function folderOptionsHtml(flatFolders: readonly FolderEntry[], esc: EscapeHtml) {
    return flatFolders
        .map(f => `<option value="${f.id}">${esc(f.path || f.name)}</option>`)
        .join('');
}

/**
 * Fetch a fresh folder tree and (re)populate the destination select and the optional inline
 * "new folder" parent select. Returns the flat folder list. Always fetching fresh is what makes a
 * just-created folder appear without a page reload.
 */
/**
 * @param {Object} options
 * @param {*} options.foldersApi
 * @param {Function} options.esc
 * @param {HTMLSelectElement|null} [options.select]       The folder picker to fill.
 * @param {HTMLSelectElement|null} [options.parentSelect]  The "create under" picker, when shown.
 * @param {*} [options.selectedId] Folder to pre-select, when the caller has one.
 */
export async function populateFolderSelects({ foldersApi, esc, select, parentSelect, selectedId }: {
    foldersApi: Pick<FolderClient, 'list'>;
    esc: EscapeHtml;
    select?: HTMLSelectElement | null;
    parentSelect?: HTMLSelectElement | null;
    selectedId?: number | string | null;
}) {
    const tree = await foldersApi.list().catch(() => []);
    const flat = flattenFolders(tree);
    const options = folderOptionsHtml(flat, esc);
    if (select) select.innerHTML = options;
    if (parentSelect) {
        parentSelect.innerHTML = `<option value="">— Root —</option>${options}`;
    }
    if (select && selectedId != null && selectedId !== '') {
        select.value = String(selectedId);
    }
    return flat;
}

/** Create a folder inline. Returns the created folder DTO ({ id, ... }). Throws on an empty name. */
export async function createFolderInline({ foldersApi, name, parentId }: {
    foldersApi: Pick<FolderClient, 'create'>;
    name: string | null | undefined;
    parentId?: number | string | null;
}) {
    const trimmed = (name || '').trim();
    if (!trimmed) {
        throw new Error('Folder name is required.');
    }
    return foldersApi.create(trimmed, parentId ? Number(parentId) : null);
}
