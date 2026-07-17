export async function applyPortalBranding() {
  try {
    const res = await fetch('/api/branding');
    if (!res.ok) return;
    const branding = await res.json();

    const name = firstValue(branding, 'DisplayName');
    const footer = firstValue(branding, 'FooterText');
    const logoUrl = firstValue(branding, 'LogoUrl');

    document.querySelectorAll('.portal-brand-name').forEach(el => {
      el.textContent = name || 'ETL-SQL Portal';
    });
    if (name) document.title = document.title.replace('ETL-SQL Portal', name).replace('ETL-SQL Portal', name);

    document.querySelectorAll('.portal-brand-footer').forEach(el => { el.textContent = footer || ''; });

    document.querySelectorAll('.topbar-brand').forEach(el => {
      const mark = el.querySelector('.brand-mark');
      const existing = el.querySelector('.portal-brand-logo');
      if (logoUrl) {
        if (existing) {
          existing.src = logoUrl;
        } else {
        const img = document.createElement('img');
        img.className = 'portal-brand-logo';
        img.src = logoUrl;
        img.alt = '';
        el.prepend(img);
        }
        mark?.classList.add('brand-mark-hidden');
      } else {
        existing?.remove();
        mark?.classList.remove('brand-mark-hidden');
      }
    });
  } catch { }
}

function firstValue(obj, key) {
  if (!obj) return '';
  const lower = key.charAt(0).toLowerCase() + key.slice(1);
  return obj[key] || obj[lower] || '';
}

export function initTheme() {
  const currentTheme = localStorage.getItem('portal-theme') || 'light';
  if (currentTheme === 'dark') {
    document.body.classList.add('theme-dark');
  } else {
    document.body.classList.remove('theme-dark');
  }

  // Bind the theme toggle button if it exists
  const toggleBtn = document.getElementById('themeToggleBtn');
  if (toggleBtn) {
    toggleBtn.addEventListener('click', () => {
      const isDark = document.body.classList.toggle('theme-dark');
      const nextTheme = isDark ? 'dark' : 'light';
      localStorage.setItem('portal-theme', nextTheme);
      
      // If we are on index.html with a report loaded in iframe, notify it!
      const iframe = document.querySelector('.report-viewer iframe');
      if (iframe && iframe.contentDocument) {
        if (isDark) {
          iframe.contentDocument.body.classList.add('theme-dark');
        } else {
          iframe.contentDocument.body.classList.remove('theme-dark');
        }
      }

      document.dispatchEvent(new CustomEvent('portal-theme-change', { detail: { theme: nextTheme, isDark } }));
    });
  }

  // Bind the mobile menu toggle button if it exists
  const menuBtn = document.getElementById('mobileMenuBtn');
  const sidebar = document.getElementById('sidebar');
  if (menuBtn && sidebar) {
    menuBtn.addEventListener('click', e => {
      e.stopPropagation();
      sidebar.classList.toggle('open');
    });
    // Click outside mobile menu closes it
    document.addEventListener('click', e => {
      if (sidebar.classList.contains('open') && !sidebar.contains(e.target) && e.target !== menuBtn) {
        sidebar.classList.remove('open');
      }
    });
  }
}
