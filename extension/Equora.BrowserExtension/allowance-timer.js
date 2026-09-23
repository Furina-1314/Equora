(() => {
  let until = 0, quota = null, quotaAt = 0, badge, label, requesting = false;
  function render() {
    const temporarySeconds = Math.max(0, Math.ceil((until - Date.now()) / 1000));
    const quotaSeconds = quota === null ? null : Math.max(0, Math.ceil(quota - (Date.now() - quotaAt) / 1000));
    if (!until && (quotaSeconds === null || quotaSeconds <= 0)) { badge?.remove(); badge = null; return; }
    if (!badge) {
      badge = document.createElement('div');
      badge.style.cssText = 'all:initial!important;position:fixed!important;top:16px!important;right:16px!important;max-width:calc(100vw - 32px)!important;z-index:2147483647!important;display:block!important;';
      const root = badge.attachShadow({ mode: 'closed' });
      label = document.createElement('div');
      label.style.cssText = 'box-sizing:border-box;max-width:100%;white-space:normal;overflow-wrap:anywhere;line-height:1.4;background:#202020;color:#fff;border-top:3px solid #c32f37;padding:12px 16px;font:14px "Segoe UI","Microsoft YaHei",sans-serif;box-shadow:0 2px 12px #0004;';
      root.append(label);
      document.documentElement.append(badge);
    }
    if (temporarySeconds) label.textContent = `Equora · 临时允许 ${Math.floor(temporarySeconds / 60).toString().padStart(2, '0')}:${(temporarySeconds % 60).toString().padStart(2, '0')}`;
    else if (until) label.textContent = 'Equora · 临时允许已结束';
    else if (quotaSeconds >= 600) label.textContent = `Equora · 今日可用 ${Math.ceil(quotaSeconds / 60)} 分钟`;
    else label.textContent = `Equora · 今日可用 ${Math.floor(quotaSeconds / 60).toString().padStart(2, '0')}:${(quotaSeconds % 60).toString().padStart(2, '0')}`;
  }
  async function sync() {
    if (requesting) return;
    requesting = true;
    try {
      const state = await chrome.runtime.sendMessage({ type: 'allowanceStatus' });
      until = state?.until > Date.now() ? state.until : 0;
      quota = Number.isFinite(state?.quotaRemainingSeconds) ? state.quotaRemainingSeconds : null;
      quotaAt = state?.quotaUpdatedUtcMs || Date.now();
      render();
    } catch { until = 0; quota = null; render(); }
    finally { requesting = false; }
  }
  void sync();
  setInterval(() => { render(); if (until && until <= Date.now()) void sync(); }, 1000);
  setInterval(sync, 5000);
})();
