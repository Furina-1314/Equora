const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const read = file => fs.readFileSync(path.join(__dirname, '../Equora.BrowserExtension', file), 'utf8');
test('Block button shows errors, opens the allowed website, and disables after expiry', async () => {
  const elements = Object.fromEntries(['domain', 'allow', 'back', 'feedback'].map(id => [id, {}]));
  let fail = true, used = false, navigated, refresh;
  vm.runInNewContext(read('blocked.js'), {
    URL, Date, EquoraPolicy: require('../Equora.BrowserExtension/policy.js'), window: {},
    location: { href: 'chrome-extension://test/blocked.html?host=example.com', replace: url => { navigated = url; } },
    document: { getElementById: id => elements[id] },
    setInterval(fn) { refresh = fn; },
    chrome: { runtime: { sendMessage: async message => {
      if (message.type === 'allowanceStatus') return { used, until: 1 };
      if (fail) throw Error('connection failed');
      return { ok: true, until: Date.now() + 300000 };
    } } }
  });
  await new Promise(resolve => setImmediate(resolve));
  await elements.allow.onclick();
  assert.match(elements.feedback.textContent, /connection failed/);
  assert.equal(elements.allow.disabled, false);
  fail = false;
  await elements.allow.onclick();
  assert.equal(navigated, 'https://example.com');
  used = true;
  await refresh();
  assert.equal(elements.allow.disabled, true);
  assert.match(elements.allow.textContent, /已用完/);
});
test('Website overlay shows the shared deadline and requests enforcement at zero', async () => {
  let now = 1000000, badge, label, messages = 0;
  const intervals = [];
  class Clock extends Date { static now() { return now; } }
  vm.runInNewContext(read('allowance-timer.js'), {
    Date: Clock,
    document: {
      createElement: () => ({ style: {}, remove() {}, attachShadow: () => ({ append: element => { label = element; } }) }),
      documentElement: { append: element => { badge = element; } }
    },
    chrome: { runtime: { sendMessage: async () => { messages++; return { until: 1300000, used: true }; } } },
    setInterval(fn) { intervals.push(fn); }
  });
  await new Promise(resolve => setImmediate(resolve));
  assert.ok(badge);
  assert.match(label.textContent, /05:00/);
  now += 1000; intervals[0]();
  assert.match(label.textContent, /04:59/);
  now += 299000; intervals[0]();
  assert.match(label.textContent, /已结束/);
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(messages, 2);
});
test('Daily quota uses minute precision until ten minutes and seconds below it', async () => {
  let now = 1000000, label;
  const intervals = [];
  class Clock extends Date { static now() { return now; } }
  vm.runInNewContext(read('allowance-timer.js'), {
    Date: Clock,
    document: {
      createElement: () => ({ style: {}, remove() {}, attachShadow: () => ({ append: element => { label = element; } }) }),
      documentElement: { append() {} }
    },
    chrome: { runtime: { sendMessage: async () => ({ quotaRemainingSeconds: 660, quotaUpdatedUtcMs: now }) } },
    setInterval(fn) { intervals.push(fn); }
  });
  await new Promise(resolve => setImmediate(resolve));
  assert.match(label.textContent, /11 分钟/);
  now += 61000; intervals[0]();
  assert.match(label.textContent, /09:59/);
});
