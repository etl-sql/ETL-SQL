export function catalogQuery(values: Record<string, unknown> = {}) {
  const query = new URLSearchParams();
  Object.entries(values).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== '') query.set(key, String(value));
  });
  return query.toString();
}

export function selectionCell(id: number | string, label: string) {
  return `<td class="catalog-select-cell"><input type="checkbox" data-select-id="${id}" aria-label="Select ${label}"></td>`;
}

export function headerSelectionCell(label: string) {
  return `<th class="catalog-select-cell"><input type="checkbox" data-select-all aria-label="Select all ${label} on this page"></th>`;
}

export function selectedIds(root: ParentNode) {
  return [...root.querySelectorAll<HTMLInputElement>('[data-select-id]:checked')].map(input => Number(input.dataset.selectId));
}

/**
 * @param {ParentNode} root
 * @param {(selectedIds: Array<number>) => void} [onChange]
 */
export function bindSelection(root: ParentNode, onChange: (ids: number[]) => void = () => {}) {
  const all = root.querySelector<HTMLInputElement>('[data-select-all]');
  const items = [...root.querySelectorAll<HTMLInputElement>('[data-select-id]')];
  const update = () => {
    if (all) {
      all.checked = items.length > 0 && items.every(item => item.checked);
      all.indeterminate = items.some(item => item.checked) && !all.checked;
    }
    onChange(selectedIds(root));
  };
  all?.addEventListener('change', () => {
    items.forEach(item => { item.checked = all.checked; });
    update();
  });
  items.forEach(item => item.addEventListener('change', update));
  update();
}

export function renderCatalogPager(root: HTMLElement, result: Partial<PagedResult<unknown>> | null | undefined, onPage: (page: number) => void) {
  const safeInt = (v: unknown) => Number.isFinite(Number(v)) ? Math.trunc(Number(v)) : 0;
  const total = safeInt(result?.total);
  const page = safeInt(result?.page) || 1;
  const pageSize = safeInt(result?.pageSize) || 25;
  const pages = Math.max(1, Math.ceil(total / pageSize));
  const start = total === 0 ? 0 : ((page - 1) * pageSize) + 1;
  const end = Math.min(total, page * pageSize);

  root.innerHTML = `
    <span class="catalog-count">${start}-${end} of ${total}</span>
    <button class="btn btn-outline btn-sm" data-page="${page - 1}" ${page <= 1 ? 'disabled' : ''}>Previous</button>
    <span class="catalog-page-label">Page ${page} of ${pages}</span>
    <button class="btn btn-outline btn-sm" data-page="${page + 1}" ${page >= pages ? 'disabled' : ''}>Next</button>`;
  root.querySelectorAll<HTMLButtonElement>('[data-page]:not([disabled])').forEach(button => {
    button.addEventListener('click', () => onPage(Number(button.dataset.page)));
  });
}
