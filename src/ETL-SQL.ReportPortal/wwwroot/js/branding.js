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
    if (name) document.title = document.title.replace('ETL-SQL Report Portal', name).replace('ETL-SQL Portal', name);

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
