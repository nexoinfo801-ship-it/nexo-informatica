import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE=path.dirname(fileURLToPath(import.meta.url));
const ROOT=path.resolve(HERE,'..','public','pdv-mobile');
const app=fs.readFileSync(path.join(ROOT,'app.js'),'utf8');
const html=fs.readFileSync(path.join(ROOT,'index.html'),'utf8');
const manifest=JSON.parse(fs.readFileSync(path.join(ROOT,'manifest.webmanifest'),'utf8'));
const sw=fs.readFileSync(path.join(ROOT,'sw.js'),'utf8');

function has(re,msg){assert.match(app,re,msg)}

test('PDV mobile uses IndexedDB v2 and durable cart store',()=>{
  has(/VER=2\b/,'DB schema must be v2');
  has(/\['products','customers','sales','outbox','cart'\]/,'cart store must exist');
  has(/state=\{products:\[\],customers:\[\],sales:\[\],outbox:\[\],cart:\[\]\}/,'cart must be application state');
});

test('cart is restored and persisted across mutations',()=>{
  has(/\['products','customers','sales','outbox','cart'\]\.map\(all\)/,'load must restore cart');
  has(/async function persistCart\(\).*replaceAll\('cart'/s,'persistCart must write IndexedDB');
  has(/async function addCart[\s\S]*await persistCart\(\)/,'add must persist cart');
  has(/async function changeQty[\s\S]*await persistCart\(\)/,'quantity change must persist cart');
  has(/#clearCart[\s\S]*await persistCart\(\)/,'clear cart must persist empty state');
  has(/state\.cart=\[\];await persistCart\(\)/,'completed sale must clear durable cart');
});

test('sales are durable and include transaction UUID before entering outbox',()=>{
  has(/transaction_uuid:crypto\.randomUUID\(\)/,'sale must have transaction UUID');
  has(/await put\('sales',sale\);state\.sales\.push\(sale\)/,'sale must be stored locally');
  has(/await queue\('sales','CREATE',sale\)/,'sale must enter outbox');
});

test('stock is decremented locally and negative stock is blocked',()=>{
  has(/p\.stock=Number\(p\.stock\)-x\.qty/,'sale must decrement stock');
  has(/Number\(p\.stock\)\+qty<0/,'manual stock move must block negative result');
  has(/if\(!reason\)return toast\('Informe o motivo do movimento\.'\)/,'stock adjustment requires reason');
});

test('product validation blocks invalid price stock and duplicate SKU EAN',()=>{
  has(/price<=0/,'price must be positive');
  has(/stock<0/,'stock must be nonnegative');
  has(/SKU já cadastrado/,'duplicate SKU must be rejected');
  has(/EAN já cadastrado/,'duplicate EAN must be rejected');
});

test('customer validation rejects malformed or duplicate CPF CNPJ identifiers',()=>{
  has(/!\[11,14\]\.includes\(doc\.length\)/,'document length validation required');
  has(/CPF\/CNPJ já cadastrado/,'duplicate document must be rejected');
});

test('outbox remains local and reconnect never auto-sends it',()=>{
  has(/status:'PENDING'/,'outbox event must be pending');
  has(/Internet voltou\. A fila local NÃO foi enviada automaticamente/,'safe reconnect message required');
  const onlineHandler=(app.match(/addEventListener\('online',[\s\S]*?\);addEventListener\('offline'/)||[''])[0];
  assert.doesNotMatch(onlineHandler,/flush|sync|send|fetch|post/i,'online handler must not transmit outbox');
});

test('core functional buttons and navigation are real DOM controls',()=>{
  for(const id of ['refreshHome','clearCart','finishSale','newProduct','stockAdjust','newCustomer','modalSave']) assert.match(html,new RegExp(`id="${id}"`),`missing ${id}`);
  for(const view of ['home','pdv','products','stock','customers','nexa']) assert.match(html,new RegExp(`data-go="${view}"`),`missing navigation ${view}`);
  has(/#newProduct'\)\.onclick=\(\)=>openProduct\(\)/,'new product handler required');
  has(/#newCustomer'\)\.onclick=\(\)=>openCustomer\(\)/,'new customer handler required');
  has(/#stockAdjust'\)\.onclick=openStockAdjust/,'stock adjustment handler required');
  has(/#finishSale'\)\.onclick=finishSale/,'finish sale handler required');
});

test('PWA contract remains standalone with service worker',()=>{
  assert.equal(manifest.short_name,'NEXO PDV');
  assert.equal(manifest.display,'standalone');
  has(/serviceWorker\.register\('\.\/sw\.js'\)/,'service worker registration required');
  assert.match(sw,/fetch/,'service worker must implement fetch handling');
});

test('central push pull ACK sync is not falsely implemented',()=>{
  assert.doesNotMatch(app,/function\s+(flushOutbox|syncPush|pushOutbox|ackOutbox)\b/,'do not pretend central sync exists');
  assert.match(html,/mantém a fila sem autoenvio/i,'UI must describe current local-only queue honestly');
});
