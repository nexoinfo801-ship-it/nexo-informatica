import test from 'node:test';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import {PostgresHubStore} from '../hub_store.mjs';

const databaseUrl=String(process.env.TEST_DATABASE_URL||'').trim();

test('POSTGRES hub persistence survives store restart and preserves dedupe/ack',{skip:!databaseUrl},async()=>{
  const suffix=crypto.randomUUID();
  const messageId=`MSG-PERSIST-${suffix}`;
  const nodeId=`cliente-${crypto.randomUUID()}`;
  const envelope={schema:'NEXO_COMM_ENVELOPE_V1',schemaVersion:1,messageId,targetRole:'CLIENTE',targetNodeId:nodeId,payload:{proof:'restart'}};
  const expiresAt=new Date(Date.now()+10*60*1000).toISOString();
  const s1=new PostgresHubStore({databaseUrl});
  await s1.init();
  const first=await s1.put({messageId,envelopeHash:'a'.repeat(64),targetRole:'CLIENTE',targetNodeId:nodeId,expiresAt,envelope});
  assert.equal(first.inserted,true);
  await s1.close();

  const s2=new PostgresHubStore({databaseUrl});
  await s2.init();
  const messages=await s2.poll({role:'CLIENTE',nodeId,afterSequence:0,limit:20});
  assert.equal(messages.length,1);
  assert.equal(messages[0].envelope.messageId,messageId);
  assert.equal(messages[0].envelope.payload.proof,'restart');
  const duplicate=await s2.put({messageId,envelopeHash:'a'.repeat(64),targetRole:'CLIENTE',targetNodeId:nodeId,expiresAt,envelope});
  assert.equal(duplicate.duplicate,true);
  assert.equal(duplicate.conflict,false);
  const conflict=await s2.put({messageId,envelopeHash:'b'.repeat(64),targetRole:'CLIENTE',targetNodeId:nodeId,expiresAt,envelope});
  assert.equal(conflict.conflict,true);
  assert.equal((await s2.ack({role:'CLIENTE',nodeId,messageIds:[messageId]})).acked,1);
  assert.equal((await s2.poll({role:'CLIENTE',nodeId,afterSequence:0,limit:20})).length,0);
  await s2.close();
});
