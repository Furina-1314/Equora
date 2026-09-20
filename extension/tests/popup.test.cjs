const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const root = path.join(__dirname, '../Equora.BrowserExtension');

test('Toolbar popup renders connection states, safely displays domains and copies its ID', async () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(root, 'manifest.json'), 'utf8'));
  const html = fs.readFileSync(path.join(root, manifest.action.default_popup), 'utf8');
  const elements = new Map(Array.from(html.matchAll(/id="([^"]+)"/g), ([, id]) => [id, {
    textContent: '', hidden: false, listeners: {},
    addEventListener(event, fn) { this.listeners[event] = fn; },
    replaceChildren(...children) { this.children = children; },
    focus() { this.focused = true; }, select() { this.selected = true; }
  }]));
  let state = { connected: false, fresh: false, blockedDomains: [] }, copied, copyFails = false;
  const context = vm.createContext({
    document: { getElementById: id => elements.get(id), createElement: () => ({ textContent: '' }) },
    chrome: { runtime: { id: 'a'.repeat(32), sendMessage: async () => state } },
    navigator: { clipboard: { writeText: async value => { if (copyFails) throw Error(); copied = value; } } },
    setInterval() {}
  });
  vm.runInContext(fs.readFileSync(path.join(root, 'popup.js'), 'utf8'), context);
  await new Promise(resolve => setImmediate(resolve));
  const get = id => elements.get(id);
  assert.match(get('status').textContent, /尚未连接/);
  assert.equal(get('extensionId').value, 'a'.repeat(32));
  state = { connected: true, fresh: true, enabled: true, blockedDomains: ['<img src=x onerror=alert(1)>'] };
  await get('refresh').listeners.click();
  assert.match(get('status').textContent, /正在限制 1/);
  assert.equal(get('domains').children[0].textContent, state.blockedDomains[0]);
  assert.equal(get('domains').hidden, false);
  state = { connected: true, fresh: true, enabled: false, blockedDomains: [] };
  await get('refresh').listeners.click();
  assert.match(get('status').textContent, /限制未启用/);
  assert.equal(get('domains').hidden, true);
  state.fresh = false;
  await get('refresh').listeners.click();
  assert.match(get('status').textContent, /状态已过期/);
  state = undefined;
  await get('refresh').listeners.click();
  assert.match(get('status').textContent, /暂时无法/);
  await get('copy').listeners.click();
  assert.equal(copied, 'a'.repeat(32));
  copyFails = true;
  await get('copy').listeners.click();
  assert.equal(get('extensionId').selected, true);
  assert.match(get('feedback').textContent, /Ctrl\+C/);
});
