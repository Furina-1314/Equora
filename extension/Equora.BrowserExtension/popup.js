const element = id => document.getElementById(id);
element('extensionId').value = chrome.runtime.id;
let refreshing = false;
async function refreshStatus() {
  if (refreshing) return;
  refreshing = true;
  try {
    const state = await chrome.runtime.sendMessage({ type: 'getStatus' });
    if (!state) throw new Error('No response');
    const domains = state.blockedDomains || [];
    element('status').textContent = !state.connected ? '尚未连接桌面应用'
      : !state.fresh ? '桌面应用未运行或状态已过期'
      : !state.enabled ? '已连接 · 限制未启用'
      : domains.length ? `已连接 · 正在限制 ${domains.length} 个网站` : '已连接 · 暂无触发的限制';
    element('description').textContent = !state.connected ? '请按下方步骤连接，并确认 Equora 正在运行。'
      : !state.fresh ? '请启动 Equora，连接恢复后会自动同步规则。'
      : !state.enabled ? '请在 Equora 中启用使用限制，或等待临时暂停结束。'
      : domains.length ? '以下域名及其子域名当前受限。临时允许的子域名仍可访问。'
      : '网站规则已同步；条件触发后自动生效。';
    element('domains').replaceChildren(...domains.map(domain => {
      const item = document.createElement('li');
      item.textContent = domain;
      return item;
    }));
    element('domains').hidden = domains.length === 0;
  } catch {
    element('status').textContent = '暂时无法读取连接状态';
    element('description').textContent = '请在浏览器的扩展管理页重新加载 Equora 扩展，再打开此面板。';
    element('domains').replaceChildren();
    element('domains').hidden = true;
  } finally { refreshing = false; }
}
element('refresh').addEventListener('click', refreshStatus);
element('copy').addEventListener('click', async () => {
  try {
    await navigator.clipboard.writeText(chrome.runtime.id);
    element('feedback').textContent = '已复制，请粘贴到 Equora 的“扩展 ID”。';
  } catch {
    element('extensionId').focus();
    element('extensionId').select();
    element('feedback').textContent = '请按 Ctrl+C 复制已选中的扩展 ID。';
  }
});
void refreshStatus();
setInterval(refreshStatus, 2000);
