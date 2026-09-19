import test from 'node:test';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import {spawn} from 'node:child_process';
import net from 'node:net';

function stable(v){if(Array.isArray(v))return v.map(stable);if(v&&typeof v==='object')return Object.fromEntries(Object.keys(v).sort().map(k=>[k,stable(v[k])]));return v}
function canonical(v){return JSON.stringify(stable(v))}
function hash(v){return crypto.createHash('sha256').update(canonical(v),'utf8').digest('hex')}
function sign(k,p){return crypto.sign('sha256',Buffer.from(canonical(p),'utf8'),{key:k,dsaEncoding:'ieee-p1363'}).toString('base64url')}
function freePort(){return new Promise((resolve,reject)=>{const s=net.createServer();s.on('error',reject);s.listen(0,'127.0.0.1',()=>{const {port}=s.address();s.close(()=>resolve(port))})})}
const serverKeys=crypto.generateKeyPairSync('ec',{namedCurve:'P-256'});
const privatePem=serverKeys.privateKey.export({type:'pkcs8',format:'pem'});
const publicJwk=serverKeys.publicKey.export({format:'jwk'});
const port=await freePort(),base=`http://127.0.0.1:${port}`;
const child=spawn(process.execPath,['server.mjs'],{cwd:new URL('..',import.meta.url),env:{...process.env,NODE_ENV:'test',HUB_PERSISTENCE_REQUIRED:'false',DATABASE_URL:'',PORT:String(port),PUBLIC_GATEWAY_URL:'https://gateway.example.test',PUBLIC_SUPPORT_URL:'https://gateway.example.test',PUBLIC_API_URL:'https://gateway.example.test',BOOTSTRAP_SIGNING_PRIVATE_KEY_PEM_B64:Buffer.from(privatePem).toString('base64'),BOOTSTRAP_SIGNING_PUBLIC_JWK:JSON.stringify(publicJwk)},stdio:['ignore','pipe','pipe']});
await new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(new Error('server timeout')),5000);child.stdout.on('data',d=>{if(String(d).includes('NEXO_GATEWAY_READY')){clearTimeout(timer);resolve()}});child.stderr.on('data',d=>{});child.on('exit',code=>reject(new Error(`server exited ${code}`)))});
test.after(()=>child.kill('SIGTERM'));

async function request(path,{method='GET',body,session}={}){const headers={};if(body!==undefined)headers['content-type']='application/json';if(session)headers['x-nexo-mobile-session']=session;const r=await fetch(base+path,{method,headers,body:body===undefined?undefined:JSON.stringify(body)});return{status:r.status,data:await r.json()}}
async function enroll(role,installId){
  const keys=crypto.generateKeyPairSync('ec',{namedCurve:'P-256'}),jwk=keys.publicKey.export({format:'jwk'});
  let r=await request('/v1/hub/enroll/challenge',{method:'POST',body:{role,installId}});assert.equal(r.status,200);
  const ch=r.data.challenge,enrollment={schema:'NEXO_HUB_ENROLLMENT_V1',compatibilityId:ch.compatibilityId,challengeId:ch.challengeId,nodeId:ch.nodeId,role:ch.role,installId:ch.installId,nonce:ch.nonce,publicJwk:jwk};
  r=await request('/v1/hub/enroll/complete',{method:'POST',body:{...enrollment,signature:sign(keys.privateKey,enrollment)}});assert.equal(r.status,200);
  return{role,nodeId:r.data.node.nodeId,credential:r.data.credential,privateKey:keys.privateKey};
}
async function hubPoll(node){const poll={schema:'NEXO_HUB_POLL_V1',nodeId:node.nodeId,role:node.role,issuedAt:new Date().toISOString(),nonce:crypto.randomBytes(12).toString('base64url'),limit:20,afterSequence:0};return request('/v1/hub/poll',{method:'POST',body:{credential:node.credential,poll,signature:sign(node.privateKey,poll)}})}
async function hubSend(node,targetRole,targetNodeId,type,payload,correlationId){const now=new Date(),expires=new Date(now.getTime()+600000),env={schema:'NEXO_COMM_ENVELOPE_V1',schemaVersion:1,compatibilityId:'NEXO-SUITE-PRIME-R8-P5-MOBILE-20260911',messageId:`MSG-${crypto.randomUUID()}`,taskId:'',correlationId,sourceRole:node.role,sourceNodeId:node.nodeId,targetRole,targetNodeId,type,createdAt:now.toISOString(),expiresAt:expires.toISOString(),payload,payloadHash:hash(payload)};return request('/v1/hub/send',{method:'POST',body:{credential:node.credential,envelope:env,signature:sign(node.privateKey,env)}})}

test('R10.26 mobile bridge completes NEXA -> pairing -> iPhone -> NEXA -> reply -> ACK',async()=>{
  const protocol=await request('/v1/mobile/protocol');assert.equal(protocol.status,200);assert.equal(protocol.data.protocol.remoteMutation,false);assert.equal(protocol.data.protocol.autoSendOnReconnect,false);
  const nexa=await enroll('NEXA','NX-NEXA-R1026');
  const issue={schema:'NEXO_MOBILE_PAIRING_ISSUE_V1',role:'NEXA',nodeId:nexa.nodeId,issuedAt:new Date().toISOString(),nonce:crypto.randomBytes(24).toString('base64url'),scopes:['support:ask','knowledge:read','tickets:read','tickets:create','status:read']};
  let r=await request('/v1/mobile/pair/issue',{method:'POST',body:{credential:nexa.credential,issue,signature:sign(nexa.privateKey,issue)}});assert.equal(r.status,200);const pairing=r.data.pairing;assert.match(pairing.pairingId,/^NXMP-/);
  r=await request('/v1/mobile/pair/claim',{method:'POST',body:{pairing,device:{deviceId:'IOS-R1026-001',label:'iPhone Teste'}}});assert.equal(r.status,200);const session=r.data.session;assert.ok(session.sessionToken);
  r=await request('/v1/mobile/pair/claim',{method:'POST',body:{pairing,device:{deviceId:'IOS-R1026-002',label:'Replay'}}});assert.notEqual(r.status,200);
  r=await request('/v1/mobile/support/ask',{method:'POST',session:session.sessionToken,body:{question:'Como abrir o caixa?',context:{channel:'IOS_PWA'}}});assert.equal(r.status,200);assert.equal(r.data.request.status,'PENDING_NEXA');const req=r.data.request;
  const np=await hubPoll(nexa);assert.equal(np.status,200);const incoming=np.data.messages.find(x=>x.envelope.correlationId===req.correlationId);assert.ok(incoming);assert.equal(incoming.envelope.type,'SUPPORT_REQUEST');
  const sent=await hubSend(nexa,'CLIENTE',req.hubNodeId,'SUPPORT_MESSAGE',{message:'Resposta real da NEXA',source:'NEXA_PRIME'},req.correlationId);assert.equal(sent.status,200);
  r=await request('/v1/mobile/support/messages?after=0',{session:session.sessionToken});assert.equal(r.status,200);const reply=r.data.messages.find(x=>x.correlationId===req.correlationId);assert.equal(reply.message,'Resposta real da NEXA');
  r=await request('/v1/mobile/support/ack',{method:'POST',session:session.sessionToken,body:{messageIds:[reply.messageId]}});assert.equal(r.status,200);assert.equal(r.data.acked,1);
  r=await request('/v1/mobile/support/tickets',{method:'POST',session:session.sessionToken,body:{subject:'Teste R10.26',description:'Chamado criado pelo iPhone.',priority:'P3'}});assert.equal(r.status,200);assert.match(r.data.ticket.protocol,/^NXT-/);
  r=await request('/v1/mobile/support/tickets',{session:session.sessionToken});assert.equal(r.status,200);assert.equal(r.data.tickets.length,1);
  r=await request('/v1/mobile/status',{session:session.sessionToken});assert.equal(r.status,200);assert.equal(r.data.status.session.paired,true);assert.equal('client' in r.data.status,false);assert.equal('master' in r.data.status,false);
});
