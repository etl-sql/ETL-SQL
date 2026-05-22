export function wireReportNav() {
  document.querySelectorAll('.report-nav-group').forEach(group => {
    const root = group.querySelector('.report-nav-root');
    const submenu = group.querySelector('.report-nav-submenu');
    if (!root || !submenu) return;

    root.addEventListener('click', event => {
      if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
      event.preventDefault();
      group.classList.toggle('open');
    });

    submenu.querySelectorAll('a').forEach(link => {
      link.addEventListener('click', () => group.classList.remove('open'));
    });
  });

  document.addEventListener('click', event => {
    if (event.target.closest('.report-nav-group')) return;
    document.querySelectorAll('.report-nav-group.open').forEach(group => group.classList.remove('open'));
  });

  document.addEventListener('keydown', event => {
    if (event.key !== 'Escape') return;
    document.querySelectorAll('.report-nav-group.open').forEach(group => group.classList.remove('open'));
  });
}
