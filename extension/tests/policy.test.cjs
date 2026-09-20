const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const policy = require('../Equora.BrowserExtension/policy.js');
test('Domain matching uses DNS label boundaries and only web schemes', () => {
  assert.equal(policy.hostname('https://EXAMPLE.com/path?private=1'), 'example.com');
  assert.equal(policy.hostname('chrome://settings'), '');
  assert.ok(policy.matches('sub.example.com', 'example.com'));
  assert.ok(!policy.matches('notexample.com', 'example.com'));
});
test('Host state blocks current tabs, temporary exception works, disconnect clears rules', async () => {
  const listener = () => ({ addListener(fn) { this.fn = fn; } });
  const port = { onMessage: listener(), onDisconnect: listener(), postMessage(message) { assert.equal(message.type, 'query'); assert.ok(!message.checksum); } };
  let rules = [], redirected = [], messages = listener();
  const chrome = {
    runtime: { connectNative: () => port, getURL: s => 'chrome-extension://test/' + s, onMessage: messages },
    declarativeNetRequest: { getSessionRules: async () => rules, updateSessionRules: async update => { rules = update.addRules || []; } },
    tabs: { query: async () => [{ id: 1, url: 'https://sub.example.com/path' }], update: async (id, value) => redirected.push(value.url), onActivated: listener(), onUpdated: listener() },
    windows: { getLastFocused: async () => ({ focused: false }) }, idle: { queryState: async () => 'active' },
    alarms: { create: async () => {}, onAlarm: listener() },
    storage: { session: { get: async () => ({}), set: async () => {} } }
  };
  const context = vm.createContext({ chrome, EquoraPolicy: policy, URL, Date, setInterval() {}, importScripts() {} });
  vm.runInContext(fs.readFileSync(path.join(__dirname, '../Equora.BrowserExtension/background.js'), 'utf8'), context);
  const settle = async () => { for (let i = 0; i < 30; i++) await Promise.resolve(); };
  await settle();
  port.onMessage.fn({ type: 'state', state: { enabled: true, blockedDomains: ['example.com'], updatedUtcMs: Date.now() } });
  await settle();
  assert.equal(rules[0].action.type, 'redirect');
  assert.equal(rules[0].action.redirect.regexSubstitution, 'chrome-extension://test/blocked.html?host=\\1');
  assert.ok(redirected[0].endsWith('?host=sub.example.com'));
  await new Promise(resolve => messages.fn({ type: 'allowTemp', host: 'sub.example.com' }, { url: 'chrome-extension://test/blocked.html?host=sub.example.com' }, resolve));
  assert.equal(rules[1].action.type, 'allow');
  port.onDisconnect.fn();
  await settle();
  assert.ok(rules.every(rule => rule.action.type === 'allow'));
});
