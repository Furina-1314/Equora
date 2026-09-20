(function (root) {
  const policy = {
    hostname(raw) { try { const url = new URL(raw); return /^https?:$/.test(url.protocol) ? url.hostname.toLowerCase().replace(/\.$/, '') : ''; } catch { return ''; } },
    matches(host, domain) { return host === domain || host.endsWith('.' + domain); }
  };
  root.EquoraPolicy = policy;
  if (typeof module !== 'undefined') module.exports = policy;
})(globalThis);
