importScripts('policy.js');
let port, lastNonce = 0, previous = null, gate = { enabled: false }, allowances = {};
let updating = Promise.resolve();
const matches = EquoraPolicy.matches;
const allowed = host => Object.entries(allowances).some(([domain, until]) => until > Date.now() && matches(host, domain));
const blocked = host => gate.enabled && gate.blockedDomains?.some(domain => matches(host, domain)) && !allowed(host);
async function refresh() {
  const rules = await chrome.declarativeNetRequest.getSessionRules();
  const domains = gate.enabled ? gate.blockedDomains || [] : [];
  const addRules = domains.length ? [{ id: 1001, priority: 1,
    action: { type: 'redirect', redirect: { regexSubstitution: chrome.runtime.getURL('blocked.html') + '?host=\\1' } },
    condition: { regexFilter: '^https?://([^/:?#]+)', requestDomains: domains, resourceTypes: ['main_frame'] }
  }] : [];
  const exceptions = Object.entries(allowances).filter(([, until]) => until > Date.now()).map(([domain]) => domain);
  if (exceptions.length) addRules.push({ id: 1002, priority: 2, action: { type: 'allow' }, condition: { requestDomains: exceptions, resourceTypes: ['main_frame'] } });
  await chrome.declarativeNetRequest.updateSessionRules({ removeRuleIds: rules.map(r => r.id), addRules });
  for (const tab of await chrome.tabs.query({})) {
    const host = EquoraPolicy.hostname(tab.url);
    if (host && blocked(host)) await chrome.tabs.update(tab.id, { url: chrome.runtime.getURL('blocked.html') + '?host=' + encodeURIComponent(host) }).catch(() => {});
  }
}
function refreshRules() { updating = updating.catch(() => {}).then(refresh); return updating; }
async function query() {
  if (!port) { connect(); return; }
  const now = Date.now();
  if (gate.updatedUtcMs && now - gate.updatedUtcMs > 12000) { gate = { enabled: false }; await refreshRules(); }
  let host = '';
  try {
    const win = await chrome.windows.getLastFocused();
    if (win.focused && await chrome.idle.queryState(60) === 'active') {
      const [tab] = await chrome.tabs.query({ active: true, windowId: win.id });
      host = EquoraPolicy.hostname(tab?.url);
    }
  } catch { }
  const seconds = previous?.host === host ? Math.min(10, Math.max(0, (now - previous.at) / 1000)) : 0;
  previous = { host, at: now };
  const message = { type: 'query', protocolVersion: 1, nonce: lastNonce = Math.max(now, lastNonce + 1) };
  if (host && seconds && gate.enabled && gate.trackedDomains?.some(d => matches(host, d))) message.usage = { domain: host, seconds };
  try { port?.postMessage(message); } catch { gate = { enabled: false }; await refreshRules(); }
}
function connect() {
  if (port) return;
  try {
    port = chrome.runtime.connectNative('com.equora.nativehost');
    port.onMessage.addListener(message => {
      if (message.type === 'state') { gate = message.state; refreshRules(); }
    });
    port.onDisconnect.addListener(() => {
      void chrome.runtime.lastError;
      port = null; previous = null; gate = { enabled: false }; refreshRules();
    });
    query();
  } catch { port = null; gate = { enabled: false }; refreshRules(); }
}
chrome.runtime.onMessage.addListener((message, sender, respond) => {
  if (!sender.url?.startsWith(chrome.runtime.getURL('blocked.html'))) return;
  if (message.type === 'allowTemp') {
    const host = EquoraPolicy.hostname('https://' + message.host);
    if (!host || host !== message.host) { respond({ ok: false }); return; }
    allowances[host] = Date.now() + 300000;
    chrome.storage.session.set({ allowances }).then(refreshRules).then(() => respond({ ok: true }));
    return true;
  }
  if (message.type === 'close' && sender.tab) chrome.tabs.remove(sender.tab.id);
});
chrome.alarms.onAlarm.addListener(() => query());
chrome.tabs.onActivated.addListener(() => query());
chrome.tabs.onUpdated.addListener((_id, change) => { if (change.url) query(); });
async function start() {
  allowances = (await chrome.storage.session.get('allowances')).allowances || {};
  await refreshRules();
  await chrome.alarms.create('equora-policy', { periodInMinutes: 0.5 });
  connect();
  setInterval(query, 5000);
}
start();
