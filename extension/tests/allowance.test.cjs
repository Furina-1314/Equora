const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const policy = require('../Equora.BrowserExtension/policy.js');
const source = fs.readFileSync(path.join(__dirname, '../Equora.BrowserExtension/background.js'), 'utf8');
function harness(storage = {}) {
  let now = 1000000, rules = [], fail = false;
  const event = () => ({ addListener(fn) { this.fn = fn; } });
  const port = { onMessage: event(), onDisconnect: event(), postMessage() {} };
  const chrome = {
    runtime: { connectNative: () => port, getURL: name => 'chrome-extension://test/' + name, onMessage: event() },
    declarativeNetRequest: { getSessionRules: async () => rules, updateSessionRules: async update => { if (fail) throw Error('rules failed'); rules = update.addRules; } },
    tabs: { query: async () => [], onActivated: event(), onUpdated: event() },
    windows: { getLastFocused: async () => ({ focused: false }) },
    alarms: { create: async () => {}, onAlarm: event() },
    storage: { session: { get: async () => ({}) }, local: { get: async () => structuredClone(storage), set: async value => Object.assign(storage, structuredClone(value)) } }
  };
  class Clock extends Date { static now() { return now; } }
  vm.runInNewContext(source, { chrome, EquoraPolicy: policy, URL, Date: Clock, importScripts() {}, setInterval() {} });
  const ready = async () => { await new Promise(resolve => setImmediate(resolve)); };
  const send = (type, host = 'sub.example.com', url = 'chrome-extension://test/blocked.html?host=' + host) => new Promise(resolve => {
    const keep = chrome.runtime.onMessage.fn({ type, host }, { url }, resolve);
    if (keep !== true) resolve(undefined);
  });
  return { ready, send, storage, rules: () => rules, advance: ms => { now += ms; }, fail: value => { fail = value; },
    state: () => port.onMessage.fn({ type: 'state', state: { enabled: true, updatedUtcMs: now, blockedDomains: ['example.com'] } }),
    alarm: () => chrome.alarms.onAlarm.fn() };
}
test('Allowance is atomic, lasts five minutes, expires, and stays consumed after restart', async () => {
  const h = harness(); await h.ready(); h.state(); await h.ready();
  const results = await Promise.all([h.send('allowTemp'), h.send('allowTemp')]);
  assert.ok(results.every(r => r.ok));
  assert.equal(results[0].until, results[1].until);
  assert.equal(results[0].until, 1300000);
  assert.ok(h.rules().some(rule => rule.action.type === 'allow'));
  const state = await h.send('allowanceStatus', '', 'https://other.example.com/path');
  assert.equal(state.until, 1300000);
  h.advance(300001); await h.alarm();
  assert.ok(h.rules().every(rule => rule.action.type !== 'allow'));
  assert.equal((await h.send('allowTemp')).ok, false);
  const restarted = harness(h.storage); await restarted.ready(); restarted.advance(300001);
  assert.equal((await restarted.send('allowTemp')).ok, false);
});
test('Rule failures return an error without consuming the allowance; web pages cannot grant access', async () => {
  const h = harness(); await h.ready(); h.state(); await h.ready(); h.fail(true);
  assert.equal((await h.send('allowTemp')).ok, false);
  assert.equal((await h.send('allowanceStatus')).used, false);
  h.fail(false);
  assert.equal(await h.send('allowTemp', 'sub.example.com', 'https://sub.example.com/'), undefined);
  assert.equal((await h.send('allowTemp')).ok, true);
});
test('Legacy redirect paths, uppercase hosts and escaped queries normalize to the same website', async () => {
  for (const raw of ['sub.example.com/path?x=1', 'SUB.EXAMPLE.COM.', 'sub.example.com:8443/path']) {
    const h = harness(); await h.ready(); h.state(); await h.ready();
    assert.equal((await h.send('allowTemp', 'sub.example.com', 'chrome-extension://test/blocked.html?host=' + encodeURIComponent(raw))).ok, true);
  }
  const h = harness(); await h.ready();
  assert.equal((await h.send('allowTemp', 'different.example', 'chrome-extension://test/blocked.html?host=example.com')).ok, false);
});
