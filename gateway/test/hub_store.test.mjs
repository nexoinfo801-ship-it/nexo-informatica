import test from 'node:test';
import assert from 'node:assert/strict';
import {MemoryHubStore,createHubStore} from '../hub_store.mjs';

function env(id,targetRole='NEXA',targetNodeId=''){
  return {schema:'NEXO_COMM_ENVELOPE_V1',schemaVersion:1,messageId:id,targetRole,targetNodeId,payload:{id}};
}

test('memory store preserves order, targeting, dedupe and ack semantics',async()=>{
  const s=new MemoryHubStore(); await s.init();
  let r=await s.put({messageId:'MSG-00000001',envelopeHash:'a'.repeat(64),targetRole:'CLIENTE',targetNodeId:'cliente-node-a',expiresAt:new Date(Date.now()+60000).toISOString(),envelope:env('MSG-00000001','CLIENTE','cliente-node-a')});
  assert.equal(r.inserted,true);
  r=await s.put({messageId:'MSG-00000001',envelopeHash:'a'.repeat(64),targetRole:'CLIENTE',targetNodeId:'cliente-node-a',expiresAt:new Date(Date.now()+60000).toISOString(),envelope:env('MSG-00000001','CLIENTE','cliente-node-a')});
  assert.equal(r.duplicate,true); assert.equal(r.conflict,false);
  r=await s.put({messageId:'MSG-00000001',envelopeHash:'b'.repeat(64),targetRole:'CLIENTE',targetNodeId:'cliente-node-a',expiresAt:new Date(Date.now()+60000).toISOString(),envelope:env('MSG-00000001','CLIENTE','cliente-node-a')});
  assert.equal(r.conflict,true);
  assert.equal((await s.poll({role:'CLIENTE',nodeId:'cliente-node-b'})).length,0);
  const a=await s.poll({role:'CLIENTE',nodeId:'cliente-node-a'}); assert.equal(a.length,1);
  assert.equal((await s.ack({role:'CLIENTE',nodeId:'cliente-node-b',messageIds:['MSG-00000001']})).acked,0);
  assert.equal((await s.ack({role:'CLIENTE',nodeId:'cliente-node-a',messageIds:['MSG-00000001']})).acked,1);
  assert.equal((await s.poll({role:'CLIENTE',nodeId:'cliente-node-a'})).length,0);
});

test('expired messages are removed and never delivered',async()=>{
  const s=new MemoryHubStore(); await s.init();
  await s.put({messageId:'MSG-EXPIRED-1',envelopeHash:'c'.repeat(64),targetRole:'NEXA',expiresAt:new Date(Date.now()-1000).toISOString(),envelope:env('MSG-EXPIRED-1')});
  assert.equal((await s.poll({role:'NEXA',nodeId:'nexa-node'})).length,0);
  assert.equal((await s.status()).total,0);
});

test('commercial persistence can be required explicitly',()=>{
  assert.throws(()=>createHubStore({databaseUrl:'',requirePersistent:true}),/HUB_PERSISTENCE_REQUIRED/);
  assert.equal(createHubStore({databaseUrl:'',requirePersistent:false}).persistence(),'MEMORY_LAB');
  assert.equal(createHubStore({databaseUrl:'postgresql:\/\/u:p@db.railway.internal:5432/x'}).persistence(),'POSTGRES');
});
