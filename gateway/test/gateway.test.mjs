import test from 'node:test';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import { spawn } from 'node:child_process';
import net from 'node:net';

function stable(value){if(Array.isArray(value))return value.map(stable);if(value&&typeof value==='object')return Object.fromEntries(Object.keys(value).sort().map(k=>[k,stable(value[k])]));return value}
function canonical(value){return JSON.stringify(stable(value))}
function hash(value){return crypto.createHash('sha256').update(canonical(value),'utf8').digest('hex')}
function sign(privateKey,payload){return crypto.sign('sha256',Buffer.from(canonical(payload),'utf8'),{key:privateKey,dsaEncoding:'ieee-p1363'}).toString('base64url')}
function freePort(){return new Promise((resolve,reject)=>{const s=net.createServer();s.on('error',reject);s.listen(0,'127.0.0.1',()=>{const {port}=s.address();s.close(()=>resolve(port))})})}
const {privateKey,publicKey}=crypto.generateKeyPairSync('ec',{namedCurve:'P-256'});
const privatePem=privateKey.export({type:'pkcs8',format:'pem'});
const jwk=publicKey.export({format:'jwk'});
const port=await freePort(),base=`http://127.0.0.1:${port}`;
const child=spawn(process.execPath,['server.mjs'],{cwd:new URL('..',import.meta.url),env:{...process.env,PORT:String(port),PUBLIC_GATEWAY_URL:'https://gateway.example.test',PUBLIC_SUPPORT_URL:'https://gateway.example.test',PUBLIC_API_URL:'https://gateway.example.test',BOOTSTRAP_SIGNING_PRIVATE_KEY_PEM_B64:Buffer.from(privatePem).toString('base64'),BOOTSTRAP_SIGNING_PUBLIC_JWK:JSON.stringify(jwk),MOBILE_LAB_ENABLED:'true'},stdio:['ignore','pipe','pipe']});
await new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(new Error('server timeout')),5000);child.stdout.on('data',d=>{if(String(d).includes('NEXO_GATEWAY_READY')){clearTimeout(timer);resolve()}});child.on('exit',code=>reject(new Error(`server exited ${code}`)))});
test.after(()=>child.kill('SIGTERM'));

async function post(path,body){const r=await fetch(base+path,{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify(body)});return {status:r.status,data:await r.json()}}
async function enroll(role,installId){
  const keys=crypto.generateKeyPairSync('ec',{namedCurve:'P-256'}),publicJwk=keys.publicKey.export({format:'jwk'});
  let r=await post('/v1/hub/enroll/challenge',{role,installId});assert.equal(r.status,200);
  const ch=r.data.challenge;
  const enrollment={schema:'NEXO_HUB_ENROLLMENT_V1',compatibilityId:ch.compatibilityId,challengeId:ch.challengeId,nodeId:ch.nodeId,role:ch.role,installId:ch.installId,nonce:ch.nonce,publicJwk};
  r=await post('/v1/hub/enroll/complete',{...enrollment,signature:sign(keys.privateKey,enrollment)});assert.equal(r.status,200);
  return {role,installId,nodeId:r.data.node.nodeId,credential:r.data.credential,privateKey:keys.privateKey,publicJwk};
}
async function sendNode(node,targetRole,type,payload,ids={}){
  const createdAt=new Date(),expiresAt=new Date(createdAt.getTime()+10*60*1000);
  const envelope={schema:'NEXO_COMM_ENVELOPE_V1',schemaVersion:1,compatibilityId:'NEXO-SUITE-PRIME-R8-P5-MOBILE-20260911',messageId:ids.messageId||`MSG-${crypto.randomUUID()}`,taskId:ids.taskId||'',correlationId:ids.correlationId||`COR-${crypto.randomUUID()}`,sourceRole:node.role,sourceNodeId:node.nodeId,targetRole,type,createdAt:createdAt.toISOString(),expiresAt:expiresAt.toISOString(),payload,payloadHash:hash(payload)};
  return post('/v1/hub/send',{credential:node.credential,envelope,signature:sign(node.privateKey,envelope)});
}
async function pollNode(node,afterSequence=0){const poll={schema:'NEXO_HUB_POLL_V1',nodeId:node.nodeId,role:node.role,issuedAt:new Date().toISOString(),nonce:crypto.randomBytes(12).toString('base64url'),limit:20,afterSequence};return post('/v1/hub/poll',{credential:node.credential,poll,signature:sign(node.privateKey,poll)})}
async function ackNode(node,messageIds){const ack={schema:'NEXO_HUB_ACK_V1',nodeId:node.nodeId,role:node.role,issuedAt:new Date().toISOString(),nonce:crypto.randomBytes(12).toString('base64url'),messageIds};return post('/v1/hub/ack',{credential:node.credential,ack,signature:sign(node.privateKey,ack)})}

test('health is ready and exposes communication hub without secrets',async()=>{const r=await fetch(`${base}/health`);assert.equal(r.status,200);const data=await r.json();assert.equal(data.ok,true);assert.equal(data.bootstrap_configured,true);assert.equal(data.upstream_configured,false);assert.equal(data.communication_hub_configured,true);assert.equal(data.compatibility_id,'NEXO-SUITE-PRIME-R8-P5-MOBILE-20260911');assert.equal(JSON.stringify(data).includes('PRIVATE KEY'),false)});
test('mobile PWA is served by gateway',async()=>{const r=await fetch(`${base}/mobile/`);assert.equal(r.status,200);const html=await r.text();assert.match(html,/NEXA Mobile/);});
test('mobile manifest is served',async()=>{const r=await fetch(`${base}/mobile/manifest.webmanifest`);assert.equal(r.status,200);const m=await r.json();assert.equal(m.short_name,'NEXA')});
test('bootstrap envelope is signed with P-256 and advertises hub',async()=>{const r=await fetch(`${base}/v1/bootstrap`);assert.equal(r.status,200);const data=await r.json();const [p,s,extra]=String(data.envelope.compact).split('.');assert.ok(p&&s&&!extra);const signature=Buffer.from(s,'base64url');assert.equal(signature.length,64);assert.equal(crypto.verify('sha256',Buffer.from(p),{key:publicKey,dsaEncoding:'ieee-p1363'},signature),true);const payload=JSON.parse(Buffer.from(p,'base64url').toString('utf8'));assert.equal(payload.communication_hub,'/v1/hub/protocol')});
test('legacy gateway still fails closed until Central upstream exists',async()=>{const r=await post('/v1/gateway',{action:'support_status'});assert.equal(r.status,503);assert.equal(r.data.error,'CENTRAL_UPSTREAM_NOT_CONFIGURED')});
test('hub protocol is message-only and remote mutation remains disabled',async()=>{const r=await fetch(`${base}/v1/hub/protocol`);assert.equal(r.status,200);const data=await r.json();assert.equal(data.protocol.remoteMutation,false);assert.equal(data.protocol.autoSendOnReconnect,false);assert.deepEqual(data.protocol.roles.sort(),['CLIENTE','MASTER','NEXA'])});

test('CLIENTE -> NEXA -> MASTER roundtrip uses one common protocol with ACK',async()=>{
  const client=await enroll('CLIENTE','NX-CLIENT-001');
  const nexa=await enroll('NEXA','NX-NEXA-001');
  const master=await enroll('MASTER','NX-MASTER-001');
  let r=await sendNode(client,'NEXA','SUPPORT_REQUEST',{subject:'Teste de comunicação',priority:'NORMAL',summary:{status:'ATENCAO'}});assert.equal(r.status,200);assert.equal(r.data.accepted,true);
  r=await pollNode(nexa);assert.equal(r.status,200);assert.equal(r.data.messages.length,1);const first=r.data.messages[0].envelope;assert.equal(first.sourceRole,'CLIENTE');assert.equal(first.targetRole,'NEXA');
  r=await ackNode(nexa,[first.messageId]);assert.equal(r.status,200);assert.equal(r.data.acked,1);
  r=await sendNode(nexa,'MASTER','SUPPORT_INTAKE',{intakeId:'NX-SUP-1',subject:'Teste de comunicação',priority:'NORMAL',sourceHash:'a'.repeat(64)});assert.equal(r.status,200);
  r=await pollNode(master);assert.equal(r.status,200);assert.equal(r.data.messages.length,1);assert.equal(r.data.messages[0].envelope.type,'SUPPORT_INTAKE');
});

test('hub rejects tampered payload hash and executable remote action types',async()=>{
  const client=await enroll('CLIENTE','NX-CLIENT-SEC');
  const createdAt=new Date(),expiresAt=new Date(createdAt.getTime()+10*60*1000);
  const envelope={schema:'NEXO_COMM_ENVELOPE_V1',schemaVersion:1,compatibilityId:'NEXO-SUITE-PRIME-R8-P5-MOBILE-20260911',messageId:`MSG-${crypto.randomUUID()}`,taskId:'',correlationId:`COR-${crypto.randomUUID()}`,sourceRole:client.role,sourceNodeId:client.nodeId,targetRole:'NEXA',type:'SUPPORT_REQUEST',createdAt:createdAt.toISOString(),expiresAt:expiresAt.toISOString(),payload:{subject:'x'},payloadHash:'0'.repeat(64)};
  let r=await post('/v1/hub/send',{credential:client.credential,envelope,signature:sign(client.privateKey,envelope)});assert.equal(r.status,400);assert.equal(r.data.error,'HUB_PAYLOAD_HASH_MISMATCH');
  r=await sendNode(client,'MASTER','EXECUTE_ACTION',{tool:'blocked'});assert.equal(r.status,400);assert.equal(r.data.error,'HUB_MESSAGE_TYPE_INVALID');
});

test('PDV ERP mobile PWA is served separately from NEXA',async()=>{const r=await fetch(`${base}/pdv-mobile/`);assert.equal(r.status,200);const html=await r.text();assert.match(html,/NEXO Mobile PDV\/ERP/);});
test('PDV ERP mobile manifest is valid',async()=>{const r=await fetch(`${base}/pdv-mobile/manifest.webmanifest`);assert.equal(r.status,200);const m=await r.json();assert.equal(m.short_name,'NEXO PDV');assert.equal(m.display,'standalone')});
