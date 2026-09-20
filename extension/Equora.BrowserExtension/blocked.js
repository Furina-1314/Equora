const host = EquoraPolicy.hostname('https://' + (new URL(location.href).searchParams.get('host') || ''));
const button = document.getElementById('allow');
const feedback = document.getElementById('feedback');
document.getElementById('domain').textContent = host;
let busy = false;
async function status() {
  if (busy) return;
  try {
    const state = await chrome.runtime.sendMessage({ type: 'allowanceStatus' });
    if (state?.used && state.until <= Date.now()) {
      button.disabled = true;
      button.textContent = '临时允许机会已用完';
      feedback.textContent = '5 分钟已结束，此网站不能再次临时允许。';
    }
  } catch { feedback.textContent = '无法连接扩展，请在扩展管理页重新加载后重试。'; }
}
document.getElementById('back').onclick = () => chrome.runtime.sendMessage({ type: 'close' }).catch(() => window.close());
button.onclick = async () => {
  if (busy || button.disabled) return;
  busy = true;
  button.disabled = true;
  feedback.textContent = '正在应用临时允许…';
  try {
    const response = await chrome.runtime.sendMessage({ type: 'allowTemp', host });
    if (!response?.ok) throw new Error(response?.error || '连接未响应，请重新加载扩展后重试。');
    location.replace('https://' + host);
  } catch (error) {
    feedback.textContent = error.message;
    button.disabled = false;
  } finally { busy = false; }
  await status();
};
void status();
setInterval(status, 1000);
