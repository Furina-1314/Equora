// Help center script: header menu (all pages), docs sidebar toggle and client-side search.

// --- Header menu (landing and generated pages) ---
const toggle = document.querySelector('.menu-toggle');
const navigation = document.querySelector('#navigation');
function closeMenu() {
  toggle.setAttribute('aria-expanded', 'false');
  toggle.setAttribute('aria-label', '打开导航');
  navigation.classList.remove('open');
}
toggle.addEventListener('click', () => {
  const expanded = toggle.getAttribute('aria-expanded') !== 'true';
  toggle.setAttribute('aria-expanded', String(expanded));
  toggle.setAttribute('aria-label', expanded ? '关闭导航' : '打开导航');
  navigation.classList.toggle('open', expanded);
});
navigation.addEventListener('click', (event) => {
  if (event.target.closest('a')) closeMenu();
});
document.addEventListener('keydown', (event) => {
  if (event.key === 'Escape' && toggle.getAttribute('aria-expanded') === 'true') {
    closeMenu();
    toggle.focus();
  }
});
window.matchMedia('(min-width: 801px)').addEventListener('change', closeMenu);

// --- Docs sidebar (generated pages only) ---
const sidebar = document.getElementById('help-sidebar');
const sidebarToggle = document.querySelector('.sidebar-toggle');
if (sidebar && sidebarToggle) {
  const setSidebar = (open) => {
    sidebar.classList.toggle('open', open);
    sidebarToggle.setAttribute('aria-expanded', String(open));
  };
  sidebarToggle.addEventListener('click', () => setSidebar(!sidebar.classList.contains('open')));
  sidebar.addEventListener('click', (event) => {
    if (event.target.closest('a')) setSidebar(false);
  });
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && sidebar.classList.contains('open')) {
      setSidebar(false);
      sidebarToggle.focus();
    }
  });
  window.matchMedia('(min-width: 1001px)').addEventListener('change', (event) => {
    if (event.matches) setSidebar(false);
  });
}

// --- Client-side search over docs/search-index.json ---
const searchInput = document.getElementById('doc-search');
const resultsPanel = document.getElementById('search-results');
if (searchInput && resultsPanel) {
  const meta = document.querySelector('meta[name="help-search-index"]');
  const indexUrl = new URL(meta.content, location.href);
  let index = [];
  let activeResult = -1;

  const escapeHtml = (value) => value.replace(/[&<>"']/g, (ch) => (
    { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch]
  ));
  const escapeRegExp = (value) => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

  function flatten(data) {
    const entries = [];
    for (const page of data.pages) {
      const base = { page: page.title, group: page.group, url: new URL(page.url, indexUrl).href };
      entries.push({ ...base, section: '', anchor: '', text: page.summary });
      for (const section of page.sections) {
        entries.push({ ...base, section: section.t, anchor: section.a, text: section.x });
      }
    }
    return entries;
  }

  function runSearch(query) {
    const q = query.toLowerCase();
    const scored = [];
    for (const item of index) {
      const inTitle = item.page.toLowerCase().includes(q);
      const inSection = Boolean(item.section) && item.section.toLowerCase().includes(q);
      const position = item.text.toLowerCase().indexOf(q);
      if (!inTitle && !inSection && position < 0) continue;
      let score = position < 0 ? 0 : 1;
      if (inSection) score += 2;
      if (inTitle) score += 3;
      if (!item.anchor) score += 1;
      scored.push({ item, score, position: position < 0 ? 0 : position });
    }
    scored.sort((a, b) => b.score - a.score);
    return scored.slice(0, 12);
  }

  function highlight(text, query) {
    const pattern = new RegExp(escapeRegExp(escapeHtml(query)), 'gi');
    return escapeHtml(text).replace(pattern, (match) => `<mark>${match}</mark>`);
  }

  function snippet(item, query, position) {
    const start = Math.max(0, position - 44);
    const end = Math.min(item.text.length, position + query.length + 90);
    const body = (start > 0 ? '…' : '') + item.text.slice(start, end) + (end < item.text.length ? '…' : '');
    return highlight(body, query);
  }

  function render(query) {
    activeResult = -1;
    if (!query.trim()) { hide(); return; }
    const matches = runSearch(query);
    if (!index.length) {
      resultsPanel.innerHTML = '<div class="search-empty">搜索索引加载中，请稍候…</div>';
      resultsPanel.hidden = false;
      return;
    }
    if (!matches.length) {
      resultsPanel.innerHTML = `<div class="search-empty">没有找到与「${escapeHtml(query)}」相关的内容</div>`;
      resultsPanel.hidden = false;
      return;
    }
    resultsPanel.innerHTML = matches.map(({ item, position }, i) => {
      const section = item.section ? `<span class="r-section">${highlight(item.section, query)}</span>` : '';
      return `<a class="search-result" data-index="${i}" href="${item.url}${item.anchor ? '#' + item.anchor : ''}">` +
        `<span class="search-result-title"><strong>${highlight(item.page, query)}</strong>${section}</span>` +
        `<span class="search-result-snippet">${snippet(item, query, position)}</span></a>`;
    }).join('');
    resultsPanel.hidden = false;
  }

  function hide() {
    resultsPanel.hidden = true;
    resultsPanel.innerHTML = '';
    activeResult = -1;
  }

  function moveActive(delta) {
    const items = resultsPanel.querySelectorAll('.search-result');
    if (!items.length) return;
    activeResult = (activeResult + delta + items.length) % items.length;
    items.forEach((item, i) => item.classList.toggle('active', i === activeResult));
    items[activeResult].scrollIntoView({ block: 'nearest' });
  }

  if (meta) {
    fetch(indexUrl).then((response) => response.json()).then((data) => { index = flatten(data); }).catch(() => {});
  }

  searchInput.addEventListener('input', () => render(searchInput.value));
  searchInput.addEventListener('focus', () => { if (searchInput.value.trim()) render(searchInput.value); });
  searchInput.addEventListener('keydown', (event) => {
    if (event.key === 'ArrowDown') { event.preventDefault(); moveActive(1); }
    else if (event.key === 'ArrowUp') { event.preventDefault(); moveActive(-1); }
    else if (event.key === 'Enter') {
      const items = resultsPanel.querySelectorAll('.search-result');
      const target = items[activeResult >= 0 ? activeResult : 0];
      if (target) target.click();
    } else if (event.key === 'Escape') {
      hide();
      searchInput.blur();
    }
  });
  resultsPanel.addEventListener('mousedown', (event) => {
    const link = event.target.closest('.search-result');
    if (link) {
      event.preventDefault();
      window.location.href = link.href;
    }
  });
  document.addEventListener('click', (event) => {
    if (!event.target.closest('.home-search, .sidebar-search')) hide();
  });
}
