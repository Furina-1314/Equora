importScripts('policy.js');
let port, lastNonce = 0, previous = null, gate = { enabled: false }, allowances = {};
let updating = Promise.resolve();
let granting = Promise.resolve();
function startOfToday() { const d = new Date(Date.now()); d.setHours(0, 0, 0, 0); return d.getTime(); }
// 临时允许按“天”配给:只统计今天发放的记录,前一天及更早的机会随新一天自动刷新。
function allowanceState(host) {
  const today = startOfToday();
  const entries = Object.entries(allowances).filter(([domain, until]) => matches(host, domain) && until >= today);
  return { until: Math.max(0, ...entries.map(([, until]) => until)), used: entries.length > 0 };
}
function quotaState(host) {
  if (!gate.enabled || !gate.updatedUtcMs || Date.now() - gate.updatedUtcMs > 12000) return {};
  const matching = (gate.quotas || []).filter(item => matches(host, item.domain));
  if (!matching.length) return {};
  return { quotaRemainingSeconds: Math.min(...matching.map(item => item.remainingSeconds)), quotaUpdatedUtcMs: gate.updatedUtcMs };
}
async function grantAllowance(host) {
  await ready;
  const domain = (gate.blockedDomains || []).filter(domain => matches(host, domain)).sort((a, b) => a.length - b.length)[0] || host;
  const existing = allowanceState(host);
  if (existing.used) return { ok: existing.until > Date.now(), ...existing, error: '此网站的今日临时允许机会已使用，明天刷新。' };
  const until = Date.now() + 300000;
  allowances[domain] = until;
  try {
    await chrome.storage.local.set({ allowances });
    await chrome.alarms.create('equora-allowance-expiry', { when: Math.min(...Object.values(allowances).filter(value => value > Date.now())) });
    await refreshRules();
    return { ok: true, until, used: true };
  } catch {
    delete allowances[domain];
    await chrome.storage.local.set({ allowances }).catch(() => {});
    await refreshRules().catch(() => {});
    return { ok: false, error: '临时允许未能生效，请重新加载扩展后重试。' };
  }
}
let receivedState = false;
function popupStatus() {
  const fresh = Boolean(port && receivedState && gate.updatedUtcMs && Date.now() - gate.updatedUtcMs <= 12000);
  return {
    connected: Boolean(port && receivedState), fresh,
    enabled: fresh && Boolean(gate.enabled),
    blockedDomains: fresh && gate.enabled ? (gate.blockedDomains || []).filter(domain => !allowed(domain)) : []
  };
}
const matches = EquoraPolicy.matches;
const allowed = host => Object.entries(allowances).some(([domain, until]) => until > Date.now() && matches(host, domain));
const blocked = host => gate.enabled && gate.blockedDomains?.some(domain => matches(host, domain)) && !allowed(host);
async function refresh() {
  const rules = await chrome.declarativeNetRequest.getSessionRules();
  const domains = gate.enabled ? gate.blockedDomains || [] : [];
  const addRules = domains.length ? [{ id: 1001, priority: 1,
    action: { type: 'redirect', redirect: { regexSubstitution: chrome.runtime.getURL('blocked.html') + '?host=\\1' } },
    condition: { regexFilter: '^https?://([^/:?#]+)(?::[0-9]+)?(?:[/?#].*)?$', requestDomains: domains, resourceTypes: ['main_frame'] }
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
      if (message.type === 'state') { receivedState = true; gate = message.state; refreshRules(); }
    });
    port.onDisconnect.addListener(() => {
      void chrome.runtime.lastError;
      port = null; receivedState = false; previous = null; gate = { enabled: false }; refreshRules();
    });
    query();
  } catch { port = null; gate = { enabled: false }; refreshRules(); }
}
chrome.runtime.onMessage.addListener((message, sender, respond) => {
  if (message.type === 'allowanceStatus') {
    const page = new URL(sender.url || 'about:blank');
    const isBlockedPage = page.href.split('?')[0].split('#')[0] === chrome.runtime.getURL('blocked.html');
    const host = isBlockedPage ? EquoraPolicy.hostname('https://' + (page.searchParams.get('host') || '')) : EquoraPolicy.hostname(sender.url);
    if (!host) return;
    ready.then(() => {
      const state = allowanceState(host);
      respond({ ...state, ...quotaState(host) });
      if (state.used && state.until <= Date.now() && blocked(host)) return refreshRules();
    }, () => respond({ error: '无法读取临时允许状态。' })).catch(() => {});
    return true;
  }
  if (sender.url === chrome.runtime.getURL('popup.html') && message.type === 'getStatus') {
    respond(popupStatus());
    void query();
    return;
  }
  const blockedUrl = new URL(sender.url || 'about:blank');
  if (blockedUrl.href.split('?')[0].split('#')[0] !== chrome.runtime.getURL('blocked.html')) return;
  if (message.type === 'allowTemp') {
    const host = EquoraPolicy.hostname('https://' + message.host);
    const sourceHost = EquoraPolicy.hostname('https://' + (blockedUrl.searchParams.get('host') || ''));
    if (!host || host !== sourceHost) { respond({ ok: false, error: '网站地址无效。' }); return; }
    granting = granting.catch(() => {}).then(() => grantAllowance(host));
    granting.then(respond, () => respond({ ok: false, error: '连接失败，请重新加载扩展后重试。' }));
    return true;
  }
  if (message.type === 'close' && sender.tab) chrome.tabs.remove(sender.tab.id);
});
chrome.alarms.onAlarm.addListener(async () => {
  await ready;
  await refreshRules();
  const pending = Object.values(allowances).filter(until => until > Date.now());
  if (pending.length) await chrome.alarms.create('equora-allowance-expiry', { when: Math.min(...pending) });
  await query();
});
chrome.tabs.onActivated.addListener(() => query());
chrome.tabs.onUpdated.addListener((_id, change) => { if (change.url) query(); });
async function start() {
  allowances = { ...((await chrome.storage.session.get('allowances')).allowances || {}), ...((await chrome.storage.local.get('allowances')).allowances || {}) };
  const today = startOfToday();
  for (const [domain, until] of Object.entries(allowances)) if (until < today) delete allowances[domain];
  await chrome.storage.local.set({ allowances }).catch(() => {});
  await chrome.storage.local.set({ allowances });
  await refreshRules();
  await chrome.alarms.create('equora-policy', { periodInMinutes: 0.5 });
  connect();
  setInterval(query, 5000);
}
const ready = start();
