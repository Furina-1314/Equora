(() => {
  let until = 0, badge, label, requesting = false;
  function render() {
    if (!until) { badge?.remove(); badge = null; return; }
    if (!badge) {
      badge = document.createElement('div');
      badge.style.cssText = 'all:initial!important;position:fixed!important;top:16px!important;right:16px!important;z-index:2147483647!important;display:block!important;';
      const root = badge.attachShadow({ mode: 'closed' });
      label = document.createElement('div');
      label.style.cssText = 'background:#202020;color:#fff;border-top:3px solid #c32f37;padding:12px 16px;font:14px "Segoe UI","Microsoft YaHei",sans-serif;box-shadow:0 2px 12px #0004;';
      root.append(label);
      document.documentElement.append(badge);
    }
    const seconds = Math.max(0, Math.ceil((until - Date.now()) / 1000));
    label.textContent = seconds ? `Equora · 临时允许 ${Math.floor(seconds / 60).toString().padStart(2, '0')}:${(seconds % 60).toString().padStart(2, '0')}` : 'Equora · 临时允许已结束';
  }
  async function sync() {
    if (requesting) return;
    requesting = true;
    try {
      const state = await chrome.runtime.sendMessage({ type: 'allowanceStatus' });
      until = state?.until > Date.now() ? state.until : 0;
      render();
    } catch { until = 0; render(); }
    finally { requesting = false; }
  }
  void sync();
  setInterval(() => { render(); if (until && until <= Date.now()) void sync(); }, 1000);
  setInterval(sync, 5000);
})();
