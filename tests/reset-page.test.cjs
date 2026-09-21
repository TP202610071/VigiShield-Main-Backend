const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// Exercise the actual shipped script without a browser, network, or real tokens.
const pagePath = process.env.RESET_PAGE_PATH || path.join(__dirname, '../web/reset.html');
const html = fs.readFileSync(pagePath, 'utf8');
const script = html.match(/<script>([\s\S]*?)<\/script>/)[1];
function load(search = '?token=test-reset-token', response = { ok: true }) {
  const elements = Object.fromEntries(['msg', 'form', 'btn', 'p1', 'p2'].map(id => [id, {
    style: {}, value: '', textContent: '', className: '', disabled: false,
    addEventListener(event, callback) { this[event] = callback; },
  }]));
  const calls = [];
  const replacements = [];
  vm.runInNewContext(script, {
    URLSearchParams,
    location: { search, pathname: '/reset', hash: '' },
    history: { replaceState(...args) { replacements.push(args); } },
    document: { getElementById(id) { return elements[id]; } },
    fetch: async (url, options) => {
      calls.push({ url, options });
      if (response instanceof Error) throw response;
      return response;
    },
  });
  return { elements, calls, replacements, async submit(p1, p2 = p1) {
    elements.p1.value = p1; elements.p2.value = p2;
    await elements.form.submit({ preventDefault() {} });
  } };
}

test('missing reset token hides the form', () => {
  const { elements, calls } = load('');
  assert.equal(elements.form.style.display, 'none');
  assert.match(elements.msg.textContent, /no es válido/);
  assert.equal(calls.length, 0);
});
test('short password does not reach the API', async () => {
  const ui = load(); await ui.submit('short');
  assert.equal(ui.calls.length, 0); assert.match(ui.elements.msg.textContent, /8 caracteres/);
});
test('mismatched confirmation does not reach the API', async () => {
  const ui = load(); await ui.submit('test-password', 'different-password');
  assert.equal(ui.calls.length, 0); assert.match(ui.elements.msg.textContent, /no coinciden/);
});
test('successful reset posts the token and new password then hides the form', async () => {
  const ui = load(); await ui.submit('test-password');
  assert.equal(ui.calls.length, 1);
  assert.equal(ui.calls[0].url, 'https://api.vigishield.app/api/auth/reset-password');
  assert.equal(ui.calls[0].options.method, 'POST');
  assert.deepEqual(JSON.parse(ui.calls[0].options.body), { token: 'test-reset-token', newPassword: 'test-password' });
  assert.equal(ui.elements.form.style.display, 'none');
  assert.match(ui.elements.msg.textContent, /iniciar sesión/);
});
test('invalid or expired token displays API error and permits retry', async () => {
  const ui = load(undefined, { ok: false, json: async () => ({ message: 'Token inválido o expirado' }) });
  await ui.submit('test-password');
  assert.equal(ui.elements.msg.textContent, 'Token inválido o expirado');
  assert.equal(ui.elements.btn.disabled, false);
});
test('unreadable API error displays fallback without crashing', async () => {
  const ui = load(undefined, { ok: false, json: async () => { throw new Error('not JSON'); } });
  await ui.submit('test-password');
  assert.match(ui.elements.msg.textContent, /caducó/);
  assert.equal(ui.elements.btn.disabled, false);
});
test('network failure permits retry and never reports success', async () => {
  const ui = load(undefined, new Error('offline')); await ui.submit('test-password');
  assert.match(ui.elements.msg.textContent, /No se pudo conectar/);
  assert.equal(ui.elements.btn.disabled, false);
});
test('reset page forbids leaking token via Referer', () => {
  assert.match(html, /<meta\s+name="referrer"\s+content="no-referrer"\s*\/?\s*>/i);
});
test('token is removed from address bar but remains available for the reset', async () => {
  const ui = load();
  assert.equal(ui.replacements.length, 1);
  assert.equal(ui.replacements[0][2], '/reset');
  await ui.submit('test-password');
  assert.equal(JSON.parse(ui.calls[0].options.body).token, 'test-reset-token');
});
