// Equora 浏览器扩展 background(MV3 service worker)。
//
// 设计原则(与桌面端一致):
//  - 扩展不持久化限制规则:每次从 Native Host 拉取当前专注状态,用
//    declarativeNetRequest 的【会话规则】(session rules)下发 —— 浏览器重启、
//    扩展崩溃、主程序异常后自然为空,不会永久阻断用户网络(可恢复限制)。
//  - 白名单优先;预算超时才拦截;拦截页提供返回任务/临时允许。
//  - 不采集页面内容、不记录浏览历史;预算计时只按域名累计分钟数。

const HOST_NAME = "com.equora.nativehost";
const RULE_ID_BLOCK = 1001;
const POLL_MS = 30_000; // 专注期间轮询 host,保证状态收敛
const ALLOW_MINUTES = 5;

let port = null;
let gate = { focusing: false };
let allowUntil = 0; // 临时允许截止 epoch ms
let timer = null;

// ---------- 与 Host 通信 ----------

function connectHost() {
  try {
    port = chrome.runtime.connectNative(HOST_NAME);
  } catch (e) {
    port = null;
    return;
  }
  port.onMessage.addListener((msg) => {
    if (msg && msg.type === "state" && msg.state) {
      gate = msg.state;
      refreshRules();
    }
  });
  port.onDisconnect.addListener(() => {
    port = null;
    // host 掉线:清空规则(可恢复限制 —— 宁可放开不可锁死)。
    gate = { focusing: false };
    refreshRules().then(scheduleReconnect);
  });
  sendQuery();
}

function sendQuery() {
  if (!port) return;
  const nonce = Date.now();
  const body = {
    type: "query",
    protocolVersion: 1,
    nonce,
    ts: nonce,
  };
  body.checksum = fnv1a(JSON.stringify(body));
  try {
    port.postMessage(body);
  } catch (e) {
    /* port 已断开,onDisconnect 会处理 */
  }
}

function scheduleReconnect() {
  setTimeout(connectHost, 10_000);
}

// ---------- 规则下发与撤销 ----------

async function refreshRules() {
  const shouldBlock = gate.focusing &&
    Array.isArray(gate.blockedDomains) && gate.blockedDomains.length > 0 &&
    Date.now() >= allowUntil;

  if (!shouldBlock) {
    await chrome.declarativeNetRequest.updateSessionRules({
      removeRuleIds: [RULE_ID_BLOCK],
    });
    return;
  }

  const conditions = gate.blockedDomains
    .filter((d) => !isAllowed(d))
    .map((d) => ({
      requestDomains: [d],
      initiatorDomains: undefined,
    }));

  if (conditions.length === 0) {
    await chrome.declarativeNetRequest.updateSessionRules({
      removeRuleIds: [RULE_ID_BLOCK],
    });
    return;
  }

  await chrome.declarativeNetRequest.updateSessionRules({
    removeRuleIds: [RULE_ID_BLOCK],
    addRules: [
      {
        id: RULE_ID_BLOCK,
        priority: 1,
        action: {
          type: "redirect",
          redirect: {
            regexSubstitution: chrome.runtime.getURL("blocked.html") + "?u=\\0",
          },
        },
        condition: {
          regexFilter: "^https?://.*",
          domainsFilter: undefined,
          requestDomains: gate.blockedDomains.filter((d) => !isAllowed(d)),
          resourceTypes: ["main_frame"],
        },
      },
    ],
  });
}

function isAllowed(domain) {
  if (!Array.isArray(gate.allowedDomains)) return false;
  const d = normalizeDomain(domain);
  return gate.allowedDomains.some((a) => {
    const n = normalizeDomain(a);
    return d === n || d.endsWith("." + n);
  });
}

function normalizeDomain(raw) {
  let s = String(raw).trim().toLowerCase();
  const scheme = s.indexOf("://");
  if (scheme >= 0) s = s.slice(scheme + 3);
  const cut = s.search(/[\/:?#]/);
  if (cut >= 0) s = s.slice(0, cut);
  if (s.startsWith("www.")) s = s.slice(4);
  return s;
}

// ---------- 预算(域名分钟数;只在 tab 可见时累计) ----------

async function tickBudget() {
  if (!gate.focusing) return;
  const tabs = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
  const tab = tabs[0];
  if (!tab || !tab.url) return;
  let host = "";
  try {
    host = new URL(tab.url).hostname;
  } catch (e) {
    return;
  }
  const domain = matchingBudgetDomain(host);
  if (!domain) return;

  const key = "budget:" + dayKey();
  const store = await chrome.storage.local.get(key);
  const spent = store[key] || {};
  spent[domain] = (spent[domain] || 0) + POLL_MS / 60_000;
  await chrome.storage.local.set({ [key]: spent });
}

function matchingBudgetDomain(host) {
  const budgets = gate.domainBudgetMinutes || {};
  const d = normalizeDomain(host);
  for (const domain of Object.keys(budgets)) {
    const n = normalizeDomain(domain);
    if (d === n || d.endsWith("." + n)) return domain;
  }
  return null;
}

function dayKey() {
  return new Date().toISOString().slice(0, 10);
}

// ---------- 消息(拦截页按钮) ----------

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (msg && msg.type === "getGate") {
    sendResponse({
      gate,
      allowUntil,
      remaining: Math.max(0, (gate.endUtcMs || 0) - Date.now()),
    });
    return true;
  }
  if (msg && msg.type === "allowTemp") {
    allowUntil = Date.now() + ALLOW_MINUTES * 60_000;
    refreshRules().then(() => sendResponse({ ok: true }));
    return true;
  }
  if (msg && msg.type === "backToTask") {
    allowUntil = 0;
    refreshRules().then(() => sendResponse({ ok: true }));
    return true;
  }
});

// ---------- 启动 ----------

connectHost();
timer = setInterval(() => {
  sendQuery();
  tickBudget();
}, POLL_MS);
