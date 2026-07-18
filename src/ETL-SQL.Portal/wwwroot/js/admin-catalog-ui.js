export function catalogQuery(values = {}) {
  const query = new URLSearchParams();
  Object.entries(values).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== '') query.set(key, String(value));
  });
  return query.toString();
}

export function selectionCell(id, label) {
  return `<td class="catalog-select-cell"><input type="checkbox" data-select-id="${id}" aria-label="Select ${label}"></td>`;
}

export function headerSelectionCell(label) {
  return `<th class="catalog-select-cell"><input type="checkbox" data-select-all aria-label="Select all ${label} on this page"></th>`;
}

export function selectedIds(root) {
  return [...root.querySelectorAll('[data-select-id]:checked')].map(input => Number(input.dataset.selectId));
}

export function bindSelection(root, onChange = () => {}) {
  const all = root.querySelector('[data-select-all]');
  const items = [...root.querySelectorAll('[data-select-id]')];
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

export function renderCatalogPager(root, result, onPage) {
  const total = Number(result?.total || 0);
  const page = Number(result?.page || 1);
  const pageSize = Number(result?.pageSize || 25);
  const pages = Math.max(1, Math.ceil(total / pageSize));
  const start = total === 0 ? 0 : ((page - 1) * pageSize) + 1;
  const end = Math.min(total, page * pageSize);

  root.innerHTML = `
    <span class="catalog-count">${start}-${end} of ${total}</span>
    <button class="btn btn-outline btn-sm" data-page="${page - 1}" ${page <= 1 ? 'disabled' : ''}>Previous</button>
    <span class="catalog-page-label">Page ${page} of ${pages}</span>
    <button class="btn btn-outline btn-sm" data-page="${page + 1}" ${page >= pages ? 'disabled' : ''}>Next</button>`;
  root.querySelectorAll('[data-page]:not([disabled])').forEach(button => {
    button.addEventListener('click', () => onPage(Number(button.dataset.page)));
  });
}
