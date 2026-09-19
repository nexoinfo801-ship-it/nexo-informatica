import test from 'node:test';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import {createCommunicationHub} from '../hub.mjs';
import {MemoryMobileStore} from '../mobile_store.mjs';
import {PostgresMobileStore} from '../mobile_store_postgres.mjs';
import {MobileGatewayService} from '../mobile_gateway.mjs';
import {createMobileHttpApi} from '../mobile_http_api.mjs';

const COMPAT='NEXO-SUITE-PRIME-R8-P5-MOBILE-20260911';
function stable(v){if(Array.isArray(v))return v.map(stable);if(v&&typeof v==='object')return Object.fromEntries(Object.keys(v).sort().map(k=>[k,stable(v[k])]));return v;}
function canonical(v){return JSON.stringify(stable(v));}
function sign(key,payload){return crypto.sign('sha256',Buffer.from(canonical(payload)),{key,dsaEncoding:'ieee-p1363'}).toString('base64url');}
function serverKeys(){const k=crypto.generateKeyPairSync('ec',{namedCurve:'P-256'});return{privateKeyB64:Buffer.from(k.privateKey.export({type:'pkcs8',format:'pem'})).toString('base64'),publicJwk:k.publicKey.export({format:'jwk'})};}

async function enroll(hub,role='NEXA',installId='NEXA-CI-01'){
  const ch=hub.challenge({role,installId}),keys=crypto.generateKeyPairSync('ec',{namedCurve:'P-256'}),publicJwk=keys.publicKey.export({format:'jwk'}),privateKeyPem=keys.privateKey.export({type:'pkcs8',format:'pem'});
  const enrollment={schema:'NEXO_HUB_ENROLLMENT_V1',compatibilityId:COMPAT,challengeId:ch.challengeId,nodeId:ch.nodeId,role,installId,nonce:ch.nonce,publicJwk};
  return{...hub.complete({...enrollment,signature:sign(privateKeyPem,enrollment)}),privateKeyPem};
}

test('R10.26 HUB trusted bridge routes mobile request only to the paired NEXA node',async()=>{
  const keys=serverKeys(),hub=createCommunicationHub({...keys,compatibilityId:COMPAT});
  await hub.ready();
  const nexa=await enroll(hub);
  const source='cliente-mobile-123456789abc';
  const put=await hub.trustedSend({sourceRole:'CLIENTE',sourceNodeId:source,targetRole:'NEXA',targetNodeId:nexa.node.nodeId,type:'SUPPORT_REQUEST',correlationId:'MOB-CI-01',payload:{schema:'NEXO_MOBILE_SUPPORT_REQUEST_V1',mobileRequestId:'NXMR-1234567890ABCDEF',deviceId:'IOS-CI',question:'Como abrir o caixa?',context:{}}});
  assert.equal(put.accepted,true);
  const polled=await hub.trustedPoll({role:'NEXA',nodeId:nexa.node.nodeId,limit:10});
  assert.equal(polled.messages.length,1);
  assert.equal(polled.messages[0].envelope.sourceNodeId,source);
  assert.equal((await hub.trustedAck({role:'NEXA',nodeId:nexa.node.nodeId,messageIds:[polled.messages[0].envelope.messageId]})).acked,1);
  await hub.close();
});

test('R10.26 authenticated NEXA creates one-time pairing and iPhone session without raw-token persistence',async()=>{
  const keys=serverKeys(),hub=createCommunicationHub({...keys,compatibilityId:COMPAT});
  await hub.ready();
  const nexa=await enroll(hub);
  const req={schema:'NEXO_MOBILE_PAIR_CREATE_V1',compatibilityId:COMPAT,nodeId:nexa.node.nodeId,role:'NEXA',issuedAt:new Date().toISOString(),nonce:crypto.randomBytes(12).toString('base64url'),scopes:['support:ask','status:read'],label:'iPhone CI'};
  const authBody={credential:nexa.credential,request:req,signature:sign(nexa.privateKeyPem,req)};
  const store=new MemoryMobileStore(),service=new MobileGatewayService({store,hub}),api=createMobileHttpApi({hub,store,service});await api.ready();
  const created=await api.handle({method:'POST',pathname:'/v1/mobile/pair/create',body:authBody});
  assert.equal(created.status,200);
  const rawPair=created.body.pairing.pairingToken;
  assert.equal(JSON.stringify(store).includes(rawPair),false);
  const claimed=await api.handle({method:'POST',pathname:'/v1/mobile/pair/claim',body:{pairing:created.body.pairing,device:{deviceId:'IOS-CI-01',label:'CI'}}});
  assert.equal(claimed.status,200);
  const rawSession=claimed.body.session.sessionToken;
  assert.equal(JSON.stringify(store).includes(rawSession),false);
  const replay=await api.handle({method:'POST',pathname:'/v1/mobile/pair/claim',body:{pairing:created.body.pairing,device:{deviceId:'IOS-CI-02'}}});
  assert.equal(replay.body.ok,false);
  await api.close();await hub.close();
});

test('R10.26 Postgres mobile store persists only hashes',async(t)=>{
  const url=String(process.env.TEST_DATABASE_URL||'').trim();if(!url)return t.skip('TEST_DATABASE_URL unavailable');
  const store=new PostgresMobileStore({databaseUrl:url});await store.init();
  const suffix=crypto.randomUUID().replaceAll('-',''),pairingId='NXMP-'+suffix,raw='RAW-PAIRING-'+suffix,hash=crypto.createHash('sha256').update(raw).digest('hex');
  await store.createPairing({pairingId,tokenHash:hash,nexaNodeId:'nexa-123456789abc',scopes:['support:ask'],label:'CI',createdAt:new Date().toISOString(),expiresAt:new Date(Date.now()+60000).toISOString(),consumedAt:null});
  const q=await store.pool.query('SELECT token_hash FROM nexo_mobile_pairings WHERE pairing_id=$1',[pairingId]);
  assert.equal(q.rows[0].token_hash,hash);assert.notEqual(q.rows[0].token_hash,raw);
  await store.pool.query('DELETE FROM nexo_mobile_pairings WHERE pairing_id=$1',[pairingId]);
  await store.close();
});
