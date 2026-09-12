import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const read=(...parts)=>fs.readFileSync(path.join(process.cwd(),...parts),'utf8');
const nexaLoader=read('public','mobile','mobile-v2.css');
const nexa34=read('public','mobile','suite-r34.css');
const nexaJs=read('public','mobile','mobile-v2.js');
const pdvLoader=read('public','pdv-mobile','app.css');
const pdv34=read('public','pdv-mobile','suite-r34.css');
const pdvJs=read('public','pdv-mobile','app.js');
const canonical=['#0b1824','#2c9de7','#21c99a','#e5ac4c','#e56572','#f5f9ff'];

test('NEXA Mobile loads R3.3 then R3.4 in runtime CSS chain',()=>{
  assert.match(nexaLoader,/suite-r33\.css/);
  assert.match(nexaLoader,/suite-r34\.css/);
  assert.ok(nexaLoader.indexOf('suite-r33.css') < nexaLoader.indexOf('suite-r34.css'));
});

test('PDV ERP Mobile loads R3.3 then R3.4 in runtime CSS chain',()=>{
  assert.match(pdvLoader,/suite-r33\.css/);
  assert.match(pdvLoader,/suite-r34\.css/);
  assert.ok(pdvLoader.indexOf('suite-r33.css') < pdvLoader.indexOf('suite-r34.css'));
});

test('both mobile products use exact canonical R3.4 semantic palette',()=>{
  for(const token of canonical){assert.ok(nexa34.includes(token),`NEXA missing ${token}`);assert.ok(pdv34.includes(token),`PDV missing ${token}`);}
});

test('R3.4 mobile layers include focus and reduced-motion accessibility',()=>{
  for(const css of [nexa34,pdv34]){assert.match(css,/focus-visible/);assert.match(css,/prefers-reduced-motion/);}
});

test('NEXA Mobile still requires explicit outbox send after reconnect',()=>{
  assert.match(nexaJs,/flushOutbox/);
  assert.doesNotMatch(nexaJs,/addEventListener\(['"]online['"][\s\S]{0,220}flushOutbox\s*\(/);
});

test('PDV Mobile keeps durable local cart and does not pretend central ACK sync exists',()=>{
  assert.match(pdvJs,/cart/);
  assert.match(pdvJs,/outbox/);
  assert.doesNotMatch(pdvJs,/centralSyncComplete|serverAckComplete|SYNC_PRODUCTION_READY/);
});
