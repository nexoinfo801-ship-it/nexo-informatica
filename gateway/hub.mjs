import crypto from 'node:crypto';

const PROTOCOL_VERSION = '1.0.0-lab';
const CREDENTIAL_TTL_MS = 30 * 24 * 60 * 60 * 1000;
const ENROLL_TTL_MS = 2 * 60 * 1000;
const MAX_MESSAGE_TTL_MS = 24 * 60 * 60 * 1000;
const MAX_PAYLOAD_BYTES = 64 * 1024;
const MAX_QUEUE_PER_ROLE = 500;
const ROLES = new Set(['CLIENTE', 'MASTER', 'NEXA']);
const MESSAGE_TYPES = new Set([
  'HEARTBEAT',
  'SUPPORT_REQUEST',
  'SUPPORT_MESSAGE',
  'SUPPORT_STATUS',
  'DIAGNOSTIC_SUMMARY',
  'SUPPORT_INTAKE',
  'LICENSE_REQUEST',
  'LICENSE_STATUS',
  'PRIME_PROPOSAL',
  'PRIME_RESULT',
  'SYSTEM_NOTICE'
]);
const FORBIDDEN_KEY = /(private.?key|password|passwd|secret|token|credential|authorization|cookie|pfx|pkcs12)/i;

function stable(value){
  if(Array.isArray(value)) return value.map(stable);
  if(value && typeof value === 'object') return Object.fromEntries(Object.keys(value).sort().map(k => [k, stable(value[k])]));
  return value;
}
function canonical(value){ return JSON.stringify(stable(value)); }
function sha256(value){ return crypto.createHash('sha256').update(typeof value === 'string' ? value : canonical(value), 'utf8').digest('hex'); }
function validP256Jwk(jwk){ return Boolean(jwk && jwk.kty === 'EC' && jwk.crv === 'P-256' && typeof jwk.x === 'string' && typeof jwk.y === 'string'); }
function verifyP256(jwk, payload, signature){
  try{
    if(!validP256Jwk(jwk) || typeof signature !== 'string' || !signature) return false;
    const key = crypto.createPublicKey({key:jwk, format:'jwk'});
    return crypto.verify('sha256', Buffer.from(canonical(payload),'utf8'), {key,dsaEncoding:'ieee-p1363'}, Buffer.from(signature,'base64url'));
  }catch{return false;}
}
function safeRole(value){ const role = String(value || '').trim().toUpperCase(); return ROLES.has(role) ? role : ''; }
function validInstallId(value){ return /^[A-Za-z0-9._:-]{3,128}$/.test(String(value || '')); }
function validNodeId(value){ return /^[a-z0-9-]{12,96}$/i.test(String(value || '')); }
function validMessageId(value){ return /^[A-Za-z0-9._:-]{8,128}$/.test(String(value || '')); }
function validCorrelationId(value){ return /^[A-Za-z0-9._:-]{0,128}$/.test(String(value || '')); }
function assertNoSecrets(value, path='$'){
  if(value === null || value === undefined) return;
  if(typeof value === 'string'){
    if(value.length > 12000) throw Object.assign(new Error('HUB_PAYLOAD_STRING_TOO_LARGE'),{status:400});
    return;
  }
  if(Array.isArray(value)){
    if(value.length > 500) throw Object.assign(new Error('HUB_PAYLOAD_ARRAY_TOO_LARGE'),{status:400});
    value.forEach((v,i)=>assertNoSecrets(v,`${path}[${i}]`));
    return;
  }
  if(typeof value === 'object'){
    for(const [k,v] of Object.entries(value)){
      if(FORBIDDEN_KEY.test(k)) throw Object.assign(new Error('HUB_SECRET_FIELD_REJECTED'),{status:400,field:`${path}.${k}`});
      assertNoSecrets(v,`${path}.${k}`);
    }
  }
}

export function createCommunicationHub({privateKeyB64, publicJwk, compatibilityId}){
  const privatePem = privateKeyB64 ? Buffer.from(String(privateKeyB64),'base64').toString('utf8') : '';
  const serverPublicJwk = publicJwk && typeof publicJwk === 'object' ? publicJwk : null;
  const enroll = new Map();
  const queues = new Map([...ROLES].map(r => [r, []]));
  const seen = new Map();
  let sequence = 0;

  function configured(){ return Boolean(privatePem && validP256Jwk(serverPublicJwk) && compatibilityId); }
  function signCompact(payload){
    if(!configured()) throw Object.assign(new Error('HUB_SIGNING_NOT_CONFIGURED'),{status:503});
    const p64 = Buffer.from(canonical(payload),'utf8').toString('base64url');
    const sig = crypto.sign('sha256', Buffer.from(p64,'utf8'), {key:privatePem,dsaEncoding:'ieee-p1363'});
    if(sig.length !== 64) throw Object.assign(new Error('HUB_SIGNING_FAILED'),{status:500});
    return `${p64}.${sig.toString('base64url')}`;
  }
  function verifyCompact(compact){
    try{
      const [p64,s64,extra] = String(compact || '').split('.');
      if(!p64 || !s64 || extra || !validP256Jwk(serverPublicJwk)) return null;
      const key = crypto.createPublicKey({key:serverPublicJwk,format:'jwk'});
      const ok = crypto.verify('sha256',Buffer.from(p64,'utf8'),{key,dsaEncoding:'ieee-p1363'},Buffer.from(s64,'base64url'));
      if(!ok) return null;
      return JSON.parse(Buffer.from(p64,'base64url').toString('utf8'));
    }catch{return null;}
  }
  function cleanup(){
    const now = Date.now();
    for(const [id,ch] of enroll) if(now > Date.parse(ch.expiresAt)) enroll.delete(id);
    for(const role of ROLES){
      const q = queues.get(role) || [];
      queues.set(role, q.filter(item => now <= Date.parse(item.envelope.expiresAt)));
    }
    for(const [id,item] of seen) if(now > item.expiresAtMs) seen.delete(id);
  }
  function protocol(){
    return {
      schema:'NEXO_COMM_PROTOCOL_V1',
      protocolVersion:PROTOCOL_VERSION,
      compatibilityId,
      roles:[...ROLES],
      messageTypes:[...MESSAGE_TYPES],
      transport:'HTTPS_POLL_ACK',
      delivery:'AT_LEAST_ONCE_WHILE_GATEWAY_PROCESS_ALIVE',
      persistence:'EPHEMERAL_LAB',
      authentication:'ECDSA_P256_SELF_ENROLLMENT_PLUS_GATEWAY_SIGNED_CREDENTIAL',
      maxPayloadBytes:MAX_PAYLOAD_BYTES,
      maxMessageTtlSeconds:MAX_MESSAGE_TTL_MS/1000,
      remoteMutation:false,
      autoSendOnReconnect:false
    };
  }
  function challenge({role,installId}){
    cleanup();
    role = safeRole(role);
    installId = String(installId || '').trim();
    if(!role) throw Object.assign(new Error('HUB_ROLE_INVALID'),{status:400});
    if(!validInstallId(installId)) throw Object.assign(new Error('HUB_INSTALL_ID_INVALID'),{status:400});
    const now = new Date(), expires = new Date(now.getTime()+ENROLL_TTL_MS);
    const item = {
      schema:'NEXO_HUB_ENROLL_CHALLENGE_V1',
      compatibilityId,
      challengeId:crypto.randomUUID(),
      nodeId:`${role.toLowerCase()}-${crypto.randomUUID()}`,
      role, installId,
      nonce:crypto.randomBytes(24).toString('base64url'),
      issuedAt:now.toISOString(), expiresAt:expires.toISOString()
    };
    enroll.set(item.challengeId,item);
    return item;
  }
  function complete(body){
    cleanup();
    if(!configured()) throw Object.assign(new Error('HUB_SIGNING_NOT_CONFIGURED'),{status:503});
    const ch = enroll.get(String(body?.challengeId || ''));
    if(!ch) throw Object.assign(new Error('HUB_ENROLL_CHALLENGE_NOT_FOUND'),{status:400});
    if(Date.now() > Date.parse(ch.expiresAt)){ enroll.delete(ch.challengeId); throw Object.assign(new Error('HUB_ENROLL_CHALLENGE_EXPIRED'),{status:400}); }
    for(const k of ['nodeId','role','installId','nonce']) if(String(body?.[k] || '') !== String(ch[k])) throw Object.assign(new Error(`HUB_ENROLL_MISMATCH_${k}`),{status:400});
    const publicKey = body?.publicJwk;
    if(!validP256Jwk(publicKey)) throw Object.assign(new Error('HUB_PUBLIC_KEY_INVALID'),{status:400});
    const enrollment = {schema:'NEXO_HUB_ENROLLMENT_V1',compatibilityId,challengeId:ch.challengeId,nodeId:ch.nodeId,role:ch.role,installId:ch.installId,nonce:ch.nonce,publicJwk:publicKey};
    if(!verifyP256(publicKey,enrollment,String(body?.signature || ''))) throw Object.assign(new Error('HUB_ENROLL_SIGNATURE_INVALID'),{status:400});
    const now = new Date(), expires = new Date(now.getTime()+CREDENTIAL_TTL_MS);
    const credentialPayload = {
      schema:'NEXO_HUB_NODE_CREDENTIAL_V1',
      compatibilityId,
      nodeId:ch.nodeId,
      role:ch.role,
      installIdHash:sha256(ch.installId),
      publicJwk:publicKey,
      issuedAt:now.toISOString(),
      expiresAt:expires.toISOString(),
      transport:'MESSAGE_ONLY',
      remoteMutation:false
    };
    const credential = signCompact(credentialPayload);
    enroll.delete(ch.challengeId);
    return {credential,node:{nodeId:ch.nodeId,role:ch.role,installId:ch.installId,expiresAt:credentialPayload.expiresAt}};
  }
  function verifyCredential(compact){
    const c = verifyCompact(compact);
    if(!c || c.schema !== 'NEXO_HUB_NODE_CREDENTIAL_V1' || c.compatibilityId !== compatibilityId) throw Object.assign(new Error('HUB_CREDENTIAL_INVALID'),{status:401});
    if(Date.now() > Date.parse(c.expiresAt || '')) throw Object.assign(new Error('HUB_CREDENTIAL_EXPIRED'),{status:401});
    if(!safeRole(c.role) || !validNodeId(c.nodeId) || !validP256Jwk(c.publicJwk)) throw Object.assign(new Error('HUB_CREDENTIAL_INVALID'),{status:401});
    if(c.remoteMutation !== false || c.transport !== 'MESSAGE_ONLY') throw Object.assign(new Error('HUB_CREDENTIAL_SCOPE_INVALID'),{status:401});
    return c;
  }
  function verifySigned(credentialCompact,payload,signature){
    const c = verifyCredential(credentialCompact);
    if(!payload || typeof payload !== 'object' || Array.isArray(payload)) throw Object.assign(new Error('HUB_SIGNED_PAYLOAD_INVALID'),{status:400});
    if(String(payload.nodeId || payload.sourceNodeId || '') !== c.nodeId) throw Object.assign(new Error('HUB_NODE_MISMATCH'),{status:403});
    const claimedRole = String(payload.role || payload.sourceRole || '').toUpperCase();
    if(claimedRole !== c.role) throw Object.assign(new Error('HUB_ROLE_MISMATCH'),{status:403});
    if(!verifyP256(c.publicJwk,payload,String(signature || ''))) throw Object.assign(new Error('HUB_REQUEST_SIGNATURE_INVALID'),{status:401});
    return c;
  }
  function validateFreshIssuedAt(value,maxSkewMs=5*60*1000){
    const ms = Date.parse(String(value || ''));
    if(!Number.isFinite(ms) || Math.abs(Date.now()-ms) > maxSkewMs) throw Object.assign(new Error('HUB_REQUEST_TIME_INVALID'),{status:400});
  }
  function send(body){
    cleanup();
    const env = body?.envelope;
    const c = verifySigned(body?.credential,env,body?.signature);
    if(env.schema !== 'NEXO_COMM_ENVELOPE_V1' || Number(env.schemaVersion)!==1) throw Object.assign(new Error('HUB_SCHEMA_INVALID'),{status:400});
    if(env.compatibilityId !== compatibilityId) throw Object.assign(new Error('HUB_COMPATIBILITY_MISMATCH'),{status:409});
    if(!validMessageId(env.messageId)) throw Object.assign(new Error('HUB_MESSAGE_ID_INVALID'),{status:400});
    if(!validCorrelationId(env.correlationId || '')) throw Object.assign(new Error('HUB_CORRELATION_ID_INVALID'),{status:400});
    const targetRole = safeRole(env.targetRole);
    if(!targetRole) throw Object.assign(new Error('HUB_TARGET_ROLE_INVALID'),{status:400});
    if(!MESSAGE_TYPES.has(String(env.type || '').toUpperCase())) throw Object.assign(new Error('HUB_MESSAGE_TYPE_INVALID'),{status:400});
    if(c.role === targetRole && env.type !== 'HEARTBEAT') throw Object.assign(new Error('HUB_LOOPBACK_REJECTED'),{status:400});
    const createdMs = Date.parse(String(env.createdAt || '')), expiresMs = Date.parse(String(env.expiresAt || ''));
    if(!Number.isFinite(createdMs)||!Number.isFinite(expiresMs)||createdMs>Date.now()+60000||expiresMs<=Date.now()||expiresMs-createdMs>MAX_MESSAGE_TTL_MS) throw Object.assign(new Error('HUB_MESSAGE_TTL_INVALID'),{status:400});
    assertNoSecrets(env.payload);
    const payloadBytes = Buffer.byteLength(canonical(env.payload ?? null));
    if(payloadBytes > MAX_PAYLOAD_BYTES) throw Object.assign(new Error('HUB_PAYLOAD_TOO_LARGE'),{status:413});
    const payloadHash = sha256(env.payload ?? null);
    if(String(env.payloadHash || '').toLowerCase() !== payloadHash) throw Object.assign(new Error('HUB_PAYLOAD_HASH_MISMATCH'),{status:400});
    const envelopeHash = sha256(env);
    const prior = seen.get(env.messageId);
    if(prior){
      if(prior.envelopeHash !== envelopeHash) throw Object.assign(new Error('HUB_MESSAGE_ID_CONFLICT'),{status:409});
      return {accepted:true,duplicate:true,messageId:env.messageId,sequence:prior.sequence};
    }
    const q = queues.get(targetRole);
    const item = {sequence:++sequence,receivedAt:new Date().toISOString(),envelope:stable(env)};
    q.push(item);
    while(q.length > MAX_QUEUE_PER_ROLE) q.shift();
    seen.set(env.messageId,{envelopeHash,sequence:item.sequence,expiresAtMs:expiresMs});
    return {accepted:true,duplicate:false,messageId:env.messageId,sequence:item.sequence,targetRole};
  }
  function poll(body){
    cleanup();
    const p = body?.poll;
    const c = verifySigned(body?.credential,p,body?.signature);
    if(p.schema !== 'NEXO_HUB_POLL_V1') throw Object.assign(new Error('HUB_POLL_SCHEMA_INVALID'),{status:400});
    validateFreshIssuedAt(p.issuedAt);
    const limit = Math.max(1,Math.min(50,Number(p.limit)||20));
    const after = Math.max(0,Number(p.afterSequence)||0);
    const q = queues.get(c.role) || [];
    const messages = q.filter(item=>item.sequence>after).slice(0,limit);
    return {role:c.role,nodeId:c.nodeId,messages,pending:q.length,lastSequence:messages.length?messages[messages.length-1].sequence:after,delivery:'AT_LEAST_ONCE'};
  }
  function ack(body){
    cleanup();
    const a = body?.ack;
    const c = verifySigned(body?.credential,a,body?.signature);
    if(a.schema !== 'NEXO_HUB_ACK_V1') throw Object.assign(new Error('HUB_ACK_SCHEMA_INVALID'),{status:400});
    validateFreshIssuedAt(a.issuedAt);
    const ids = Array.isArray(a.messageIds) ? [...new Set(a.messageIds.map(String).filter(validMessageId))].slice(0,100) : [];
    if(!ids.length) throw Object.assign(new Error('HUB_ACK_EMPTY'),{status:400});
    const before = (queues.get(c.role)||[]).length;
    queues.set(c.role,(queues.get(c.role)||[]).filter(item=>!ids.includes(String(item.envelope.messageId))));
    return {acked:before-(queues.get(c.role)||[]).length,role:c.role,messageIds:ids};
  }
  function status(){ cleanup(); return {configured:configured(),protocolVersion:PROTOCOL_VERSION,compatibilityId,remoteMutation:false,persistence:'EPHEMERAL_LAB',queues:Object.fromEntries([...ROLES].map(r=>[r,(queues.get(r)||[]).length]))}; }

  return {configured,protocol,challenge,complete,send,poll,ack,status};
}
