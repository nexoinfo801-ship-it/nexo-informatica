import crypto from 'node:crypto';

export const VERSION='1.0.0-r10.26-lab';
export const COMPATIBILITY_ID='NEXO-SUITE-PRIME-R8-P5-MOBILE-20260911';
export const PROTOCOL_SCHEMA='NEXO_MOBILE_COMPANION_PROTOCOL_V1';
export const PAIRING_SCHEMA='NEXO_MOBILE_PAIRING_V1';
export const SESSION_SCHEMA='NEXO_MOBILE_SESSION_V1';
export const PAIR_ISSUE_SCHEMA='NEXO_MOBILE_PAIRING_ISSUE_V1';
export const MAX_PAIRING_WINDOW_MS=10*60*1000;
export const MAX_SESSION_WINDOW_MS=24*60*60*1000;
export const MAX_PAYLOAD_BYTES=64*1024;
export const ALLOWED_SCOPES=Object.freeze(['support:ask','knowledge:read','tickets:read','tickets:create','status:read','approval:submit']);

const SECRET_KEY=/(password|passwd|private.?key|api.?key|secret|credential|authorization|cookie|pfx|p12|pkcs12|pem|master.?key|claim.?secret)/i;
const SECRET_VALUE=/(-----BEGIN (?:EC |RSA |)PRIVATE KEY-----|\bsk-(?:proj-)?[A-Za-z0-9_-]{20,}\b|\bAIza[0-9A-Za-z_-]{30,}\b|\bBearer\s+[A-Za-z0-9._~+\/-]{24,})/i;

function stable(value){if(Array.isArray(value))return value.map(stable);if(value&&typeof value==='object')return Object.fromEntries(Object.keys(value).sort().map(k=>[k,stable(value[k])]));return value;}
function canonical(value){return JSON.stringify(stable(value));}
function sha256(value){return crypto.createHash('sha256').update(typeof value==='string'?value:canonical(value),'utf8').digest('hex');}
function safeId(value,re=/^[A-Za-z0-9._:-]{3,128}$/){const s=String(value||'').trim();if(!re.test(s))throw Object.assign(new Error('MOBILE_ID_INVALID'),{status:400});return s;}
function cleanScopes(scopes){if(!Array.isArray(scopes)||!scopes.length)throw Object.assign(new Error('MOBILE_SCOPES_REQUIRED'),{status:400});const out=[];for(const raw of scopes){const s=String(raw||'').trim();if(!ALLOWED_SCOPES.includes(s))throw Object.assign(new Error(`MOBILE_SCOPE_REJECTED:${s}`),{status:403});if(!out.includes(s))out.push(s);}return out;}
function assertSafePayload(value,path='$'){
  if(value===null||value===undefined)return true;
  if(typeof value==='string'){if(Buffer.byteLength(value,'utf8')>MAX_PAYLOAD_BYTES)throw Object.assign(new Error('MOBILE_PAYLOAD_TOO_LARGE'),{status:413});if(SECRET_VALUE.test(value))throw Object.assign(new Error('MOBILE_SECRET_VALUE_REJECTED'),{status:400});return true;}
  if(typeof value==='number'||typeof value==='boolean')return true;
  if(Array.isArray(value)){if(value.length>500)throw Object.assign(new Error('MOBILE_PAYLOAD_TOO_LARGE'),{status:413});value.forEach((v,i)=>assertSafePayload(v,`${path}[${i}]`));return true;}
  if(typeof value==='object'){for(const[k,v]of Object.entries(value)){if(SECRET_KEY.test(k))throw Object.assign(new Error(`MOBILE_SECRET_FIELD_REJECTED:${path}.${k}`),{status:400});assertSafePayload(v,`${path}.${k}`);}if(Buffer.byteLength(canonical(value),'utf8')>MAX_PAYLOAD_BYTES)throw Object.assign(new Error('MOBILE_PAYLOAD_TOO_LARGE'),{status:413});return true;}
  throw Object.assign(new Error('MOBILE_PAYLOAD_TYPE_REJECTED'),{status:400});
}
function randomToken(randomBytes,n=32){return randomBytes(n).toString('base64url');}
function requestId(prefix='NXM'){return `${prefix}-${crypto.randomUUID()}`;}
function freshIso(ms){return new Date(ms).toISOString();}

class MemoryMobileStore{
  constructor({now=()=>Date.now()}={}){this.now=now;this.pairings=new Map();this.sessions=new Map();this.tickets=new Map();}
  async init(){return{ok:true,persistence:'MEMORY'};}
  async close(){}
  persistence(){return'MEMORY';}
  cleanup(){const t=this.now();for(const[k,v]of this.pairings)if(Date.parse(v.expiresAt)<=t&&!v.usedAt)this.pairings.delete(k);for(const[k,v]of this.sessions)if(Date.parse(v.expiresAt)<=t||v.revokedAt)this.sessions.delete(k);}
  async putPairing(rec){this.cleanup();this.pairings.set(rec.pairingId,{...rec});}
  async consumePairing(pairingId,tokenHash){this.cleanup();const rec=this.pairings.get(pairingId);if(!rec)throw Object.assign(new Error('MOBILE_PAIRING_NOT_FOUND'),{status:404});if(rec.usedAt)throw Object.assign(new Error('MOBILE_PAIRING_USED'),{status:409});if(Date.parse(rec.expiresAt)<=this.now())throw Object.assign(new Error('MOBILE_PAIRING_EXPIRED'),{status:410});if(rec.tokenHash!==tokenHash)throw Object.assign(new Error('MOBILE_PAIRING_TOKEN_INVALID'),{status:401});rec.usedAt=freshIso(this.now());return{...rec};}
  async putSession(rec){this.cleanup();this.sessions.set(rec.tokenHash,{...rec});}
  async getSession(tokenHash){this.cleanup();const rec=this.sessions.get(tokenHash);return rec?{...rec}:null;}
  async putTicket(rec){this.tickets.set(rec.protocol,{...rec});return{...rec};}
  async listTickets(deviceId){return[...this.tickets.values()].filter(x=>x.deviceId===deviceId).sort((a,b)=>String(b.createdAt).localeCompare(String(a.createdAt))).map(x=>({...x}));}
  snapshot(){return{pairings:[...this.pairings.values()].map(x=>({...x})),sessions:[...this.sessions.values()].map(x=>({...x})),tickets:[...this.tickets.values()].map(x=>({...x}))};}
}

export function createMobileCompanion({hub,store=null,now=()=>Date.now(),randomBytes=crypto.randomBytes,sessionTtlMs=12*60*60*1000,pairingTtlMs=8*60*1000}={}){
  if(!hub)throw new Error('MOBILE_HUB_REQUIRED');
  store=store||new MemoryMobileStore({now});
  pairingTtlMs=Math.max(60_000,Math.min(MAX_PAIRING_WINDOW_MS,Number(pairingTtlMs)||8*60*1000));
  sessionTtlMs=Math.max(5*60_000,Math.min(MAX_SESSION_WINDOW_MS,Number(sessionTtlMs)||12*60*60*1000));
  let ready=false;
  async function init(){const r=await store.init();ready=!!r?.ok;return r;}
  async function close(){ready=false;await store.close?.();}
  function protocol(){return{schema:PROTOCOL_SCHEMA,protocolVersion:'1.1.0-lab',compatibilityId:COMPATIBILITY_ID,transport:'HTTPS_HUB_BRIDGE',delivery:'ASYNC_POLL_ACK',scopes:[...ALLOWED_SCOPES],remoteMutation:false,autoSendOnReconnect:false,maxPayloadBytes:MAX_PAYLOAD_BYTES,pairingTtlSeconds:pairingTtlMs/1000,sessionTtlSeconds:sessionTtlMs/1000,persistence:store.persistence?.()||'UNKNOWN'};}
  function ensureReady(){if(!ready)throw Object.assign(new Error('MOBILE_STORE_NOT_READY'),{status:503});}
  function validateIssue(issue){if(!issue||issue.schema!==PAIR_ISSUE_SCHEMA)throw Object.assign(new Error('MOBILE_PAIRING_ISSUE_SCHEMA_INVALID'),{status:400});if(String(issue.role||'').toUpperCase()!=='NEXA')throw Object.assign(new Error('MOBILE_PAIRING_ISSUER_ROLE_INVALID'),{status:403});safeId(issue.nodeId,/^nexa-[A-Za-z0-9-]{7,90}$/i);const issued=Date.parse(String(issue.issuedAt||''));if(!Number.isFinite(issued)||Math.abs(now()-issued)>5*60*1000)throw Object.assign(new Error('MOBILE_PAIRING_ISSUE_TIME_INVALID'),{status:400});if(!/^[A-Za-z0-9_-]{16,128}$/.test(String(issue.nonce||'')))throw Object.assign(new Error('MOBILE_PAIRING_ISSUE_NONCE_INVALID'),{status:400});return cleanScopes(issue.scopes);}
  async function issuePairing(body){ensureReady();const scopes=validateIssue(body?.issue);hub.verifyNodeSigned({credential:body?.credential,payload:body?.issue,signature:body?.signature,expectedRole:'NEXA'});const token=randomToken(randomBytes,32),pairingId=`NXMP-${randomToken(randomBytes,18)}`,createdAt=freshIso(now()),expiresAt=freshIso(now()+pairingTtlMs),hubNodeId=`cliente-mobile-${randomToken(randomBytes,18).toLowerCase()}`;const rec={pairingId,tokenHash:sha256(token),createdAt,expiresAt,scopes,issuerNodeId:String(body.issue.nodeId),hubNodeId,usedAt:null};await store.putPairing(rec);return{ok:true,pairing:{schema:PAIRING_SCHEMA,schemaVersion:1,compatibilityId:COMPATIBILITY_ID,pairingId,pairingToken:token,createdAt,expiresAt,scopes}};}
  async function claimPairing({pairing,device}={}){ensureReady();if(!pairing||pairing.schema!==PAIRING_SCHEMA||Number(pairing.schemaVersion)!==1||pairing.compatibilityId!==COMPATIBILITY_ID)throw Object.assign(new Error('MOBILE_PAIRING_SCHEMA_INVALID'),{status:400});safeId(pairing.pairingId,/^NXMP-[A-Za-z0-9_-]{16,96}$/);if(!/^[A-Za-z0-9_-]{40,128}$/.test(String(pairing.pairingToken||'')))throw Object.assign(new Error('MOBILE_PAIRING_TOKEN_INVALID'),{status:401});const deviceId=safeId(device?.deviceId),label=String(device?.label||'iPhone').trim().slice(0,80);assertSafePayload({deviceId,label});const rec=await store.consumePairing(pairing.pairingId,sha256(pairing.pairingToken));const token=randomToken(randomBytes,36),expiresAt=freshIso(now()+sessionTtlMs);const sessionRec={tokenHash:sha256(token),deviceId,label,hubNodeId:rec.hubNodeId,scopes:rec.scopes,createdAt:freshIso(now()),expiresAt,revokedAt:null};await store.putSession(sessionRec);return{ok:true,session:{schema:SESSION_SCHEMA,sessionToken:token,expiresAt,deviceId,scopes:[...rec.scopes]}};}
  async function authenticate(token,scope=''){ensureReady();if(!/^[A-Za-z0-9._~-]{32,512}$/.test(String(token||'')))throw Object.assign(new Error('MOBILE_SESSION_INVALID'),{status:401});const session=await store.getSession(sha256(String(token)));if(!session)throw Object.assign(new Error('MOBILE_SESSION_INVALID'),{status:401});if(Date.parse(session.expiresAt)<=now())throw Object.assign(new Error('MOBILE_SESSION_EXPIRED'),{status:401});if(scope&&!session.scopes.includes(scope))throw Object.assign(new Error(`MOBILE_SCOPE_REQUIRED:${scope}`),{status:403});return session;}
  async function ask(token,{question,context={}}={}){const session=await authenticate(token,'support:ask');question=String(question||'').trim().slice(0,4000);if(!question)throw Object.assign(new Error('MOBILE_QUESTION_REQUIRED'),{status:400});assertSafePayload({question,context});const id=requestId('NXM'),correlationId=`MOB-${crypto.randomUUID()}`;const payload={message:question,context:{...context,channel:'IOS_PWA',deviceId:session.deviceId},mobileRequestId:id};assertSafePayload(payload);const sent=await hub.trustedSend({sourceRole:'CLIENTE',sourceNodeId:session.hubNodeId,targetRole:'NEXA',targetNodeId:'',type:'SUPPORT_REQUEST',correlationId,payload,ttlSeconds:3600});return{ok:true,request:{id,status:'PENDING_NEXA',correlationId,hubNodeId:session.hubNodeId,messageId:sent.messageId||null}};}
  async function pollMessages(token,{afterSequence=0,limit=20}={}){const session=await authenticate(token,'support:ask');const r=await hub.trustedPoll({role:'CLIENTE',nodeId:session.hubNodeId,afterSequence:Math.max(0,Number(afterSequence)||0),limit:Math.max(1,Math.min(50,Number(limit)||20))});const messages=[];for(const item of r.messages||[]){const env=item?.envelope||{};if(env.targetNodeId&&env.targetNodeId!==session.hubNodeId)continue;if(!['SUPPORT_MESSAGE','SUPPORT_STATUS','SYSTEM_NOTICE'].includes(String(env.type||'')))continue;assertSafePayload(env.payload||{});messages.push({sequence:Number(item.sequence)||0,messageId:String(env.messageId||''),type:String(env.type||''),correlationId:String(env.correlationId||''),message:String(env.payload?.message||env.payload?.summary||'').slice(0,8000),payload:env.payload||{}});}return{ok:true,messages,lastSequence:Number(r.lastSequence)||Number(afterSequence)||0};}
  async function ackMessages(token,messageIds=[]){const session=await authenticate(token,'support:ask');const ids=[...new Set((Array.isArray(messageIds)?messageIds:[]).map(String).filter(x=>/^[A-Za-z0-9._:-]{8,128}$/.test(x)))].slice(0,100);if(!ids.length)throw Object.assign(new Error('MOBILE_ACK_EMPTY'),{status:400});const r=await hub.trustedAck({role:'CLIENTE',nodeId:session.hubNodeId,messageIds:ids});return{ok:true,acked:Number(r.acked)||0,messageIds:ids};}
  async function createTicket(token,ticket={}){const session=await authenticate(token,'tickets:create');const subject=String(ticket.subject||'').trim().slice(0,120),description=String(ticket.description||'').trim().slice(0,4000),priority=String(ticket.priority||'P4').toUpperCase();if(!subject||!description)throw Object.assign(new Error('MOBILE_TICKET_FIELDS_REQUIRED'),{status:400});if(!/^P[1-4]$/.test(priority))throw Object.assign(new Error('MOBILE_TICKET_PRIORITY_INVALID'),{status:400});assertSafePayload({subject,description,priority});const protocol=`NXT-${new Date(now()).toISOString().slice(0,10).replaceAll('-','')}-${randomToken(randomBytes,6).slice(0,8).toUpperCase()}`,correlationId=`TKT-${crypto.randomUUID()}`,rec={protocol,deviceId:session.deviceId,hubNodeId:session.hubNodeId,subject,description,priority,status:'ABERTO',createdAt:freshIso(now()),updatedAt:freshIso(now()),correlationId};await store.putTicket(rec);await hub.trustedSend({sourceRole:'CLIENTE',sourceNodeId:session.hubNodeId,targetRole:'NEXA',targetNodeId:'',type:'SUPPORT_REQUEST',correlationId,payload:{subject,description,priority,ticketProtocol:protocol,channel:'IOS_PWA'},ttlSeconds:24*60*60});return{ok:true,ticket:{protocol,subject,priority,status:'ABERTO',createdAt:rec.createdAt}};}
  async function listTickets(token){const session=await authenticate(token,'tickets:read');const rows=await store.listTickets(session.deviceId);return{ok:true,tickets:rows.map(x=>({protocol:x.protocol,subject:x.subject,priority:x.priority,status:x.status,createdAt:x.createdAt,updatedAt:x.updatedAt}))};}
  async function status(token){const session=await authenticate(token,'status:read');const hs=await hub.status();return{ok:true,status:{gateway:'ONLINE',hub:{ready:hs.ready===true,persistence:String(hs.persistence||'UNKNOWN'),protocolVersion:String(hs.protocolVersion||''),queues:hs.queues||{},total:Number(hs.total)||0},session:{active:true,paired:true,deviceId:session.deviceId,expiresAt:session.expiresAt,scopes:[...session.scopes]},remoteMutation:false,autoSendOnReconnect:false}};}
  async function testInjectSession(s){const rec={tokenHash:sha256(s.sessionToken),deviceId:s.deviceId,hubNodeId:`cliente-mobile-${randomToken(randomBytes,18).toLowerCase()}`,scopes:cleanScopes(s.scopes),createdAt:freshIso(now()),expiresAt:s.expiresAt,revokedAt:null,label:'test'};await store.putSession(rec);return true;}
  return{ready:init,close,protocol,issuePairing,claimPairing,ask,pollMessages,ackMessages,createTicket,listTickets,status,_testSnapshot:()=>store.snapshot?.()||{},_testInjectSession:testInjectSession};
}

export {MemoryMobileStore,assertSafePayload,cleanScopes,sha256};
