const host = new URL(location.href).searchParams.get('host') || '';
document.getElementById('domain').textContent = host;
document.getElementById('back').onclick = () => chrome.runtime.sendMessage({ type: 'close' });
document.getElementById('allow').onclick = async () => {
  const response = await chrome.runtime.sendMessage({ type: 'allowTemp', host });
  if (response?.ok) location.replace('https://' + host);
};
