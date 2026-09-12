import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

const read = (p) => fs.readFileSync(new URL(`../${p}`, import.meta.url), 'utf8');
const nexaCss = read('public/mobile/mobile-v2.css');
const nexaTheme = read('public/mobile/suite-r33.css');
const nexaJs = read('public/mobile/mobile-v2.js');
const pdvCss = read('public/pdv-mobile/app.css');
const pdvTheme = read('public/pdv-mobile/suite-r33.css');
const pdvJs = read('public/pdv-mobile/app.js');

const canonical = ['#2c9de7','#21c99a','#e5ac4c','#e56572','#0b1824'];

test('NEXA Mobile loads R3.3 suite theme only on LAB branch', () => {
  assert.match(nexaCss, /^@import url\("\.\/suite-r33\.css"\);/);
  for (const token of canonical) assert.ok(nexaTheme.includes(token), token);
});

test('PDV ERP Mobile loads the same R3.3 semantic palette', () => {
  assert.match(pdvCss, /^@import url\("\.\/suite-r33\.css"\);/);
  for (const token of canonical) assert.ok(pdvTheme.includes(token), token);
});

test('NEXA Mobile preserves explicit-send reconnect policy', () => {
  assert.ok(nexaJs.includes('autoSendOnReconnect'));
  assert.ok(!/addEventListener\(['"]online['"][\s\S]{0,500}(flushOutbox|hubFlush|sendPending)\s*\(/.test(nexaJs));
});

test('PDV Mobile remains durable local-first', () => {
  assert.match(pdvJs, /VER=2/);
  assert.ok(pdvJs.includes("'cart'"));
  assert.ok(pdvJs.includes('transaction_uuid'));
  assert.ok(pdvJs.includes("status:'PENDING'"));
  assert.ok(!/addEventListener\(['"]online['"][\s\S]{0,500}(flush|push|send)\s*\(/i.test(pdvJs));
});

test('R3.3 mobile themes include focus and reduced-motion accessibility', () => {
  for (const css of [nexaTheme,pdvTheme]) {
    assert.match(css, /:focus-visible/);
    assert.match(css, /prefers-reduced-motion:reduce/);
  }
});
