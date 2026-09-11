import http from 'node:http';
import crypto from 'node:crypto';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { URL } from 'node:url';
import { createCommunicationHub } from './hub.mjs';

const PORT = Number(process.env.PORT);
if (!Number.isInteger(PORT) || PORT < 1 || PORT > 65535) throw new Error('PORT_REQUIRED');

const HERE = path.dirname(fileURLToPath(import.meta.url));
const MOBILE_ROOT = path.join(HERE, 'public', 'mobile');
const PDV_MOBILE_ROOT = path.join(HERE, 'public', 'pdv-mobile');
const MAX_BODY_BYTES = Math.max(16_384, Number(process.env.MAX_BODY_BYTES || 262_144));
const RATE_LIMIT_WINDOW_MS = Math.max(10_000, Number(process.env.RATE_LIMIT_WINDOW_MS || 60_000));
const RATE_LIMIT_MAX = Math.max(10, Number(process.env.RATE_LIMIT_MAX || 120));
const BOOTSTRAP_VERSION = Math.max(1, Number(process.env.BOOTSTRAP_VERSION || 1));
const PUBLIC_GATEWAY_URL = String(process.env.PUBLIC_GATEWAY_URL || '').trim();
const PUBLIC_SUPPORT_URL = String(process.env.PUBLIC_SUPPORT_URL || PUBLIC_GATEWAY_URL).trim();
const PUBLIC_API_URL = String(process.env.PUBLIC_API_URL || PUBLIC_GATEWAY_URL).trim();
const UPSTREAM_URL = String(process.env.NEXO_UPSTREAM_URL || '').trim();
const PRIVATE_KEY_B64 = String(process.env.BOOTSTRAP_SIGNING_PRIVATE_KEY_PEM_B64 || '').trim();
const PUBLIC_JWK_RAW = String(process.env.BOOTSTRAP_SIGNING_PUBLIC_JWK || '').trim();
const MOBILE_LAB_ENABLED = String(process.env.MOBILE_LAB_ENABLED || '').toLowerCase() === 'true';
const COMPATIBILITY_ID = 'NEXO-SUITE-PRIME-R8-P5-MOBILE-20260911';

function json(res,status,body,extra={}){
  const raw=JSON.stringify(body);
  res.writeHead(status,{
    'content-type':'application/json; charset=utf-8','content-length':Buffer.byteLength(raw),
    'cache-control':'no-store','x-content-type-options':'nosniff','x-frame-options':'DENY',
    'referrer-policy':'no-referrer','permissions-policy':'camera=(self), microphone=(), geolocation=()',
    'content-security-policy':"default-src 'none'; frame-ancestors 'none'",
    'access-control-allow-origin':'*','access-control-allow-methods':'GET,POST,OPTIONS',
    'access-control-allow-headers':'content-type,x-nexo-request-id',...extra
  });res.end(raw);
}
function stable(value){if(Array.isArray(value))return value.map(stable);if(value&&typeof value==='object')return Object.fromEntries(Object.keys(value).sort().map(k=>[k,stable(value[k])]));return value}
function publicJwk(){if(!PUBLIC_JWK_RAW)return null;try{return JSON.parse(PUBLIC_JWK_RAW)}catch{return null}}
function canonical(value){return JSON.stringify(stable(value))}
function signPayload(payload){
  if(!PRIVATE_KEY_B64)throw new Error('BOOTSTRAP_SIGNING_KEY_NOT_CONFIGURED');
  const pem=Buffer.from(PRIVATE_KEY_B64,'base64').toString('utf8');
  const p64=Buffer.from(JSON.stringify(stable(payload)),'utf8').toString('base64url');
  const signature=crypto.sign('sha256',Buffer.from(p64,'utf8'),{key:pem,dsaEncoding:'ieee-p1363'});
  if(signature.length!==64)throw new Error('BOOTSTRAP_SIGNATURE_INVALID');
  return `${p64}.${signature.toString('base64url')}`;
}
function validHttpsUrl(value){try{const u=new URL(value);return u.protocol==='https:'&&!u.username&&!u.password}catch{return false}}
function bootstrapEnvelope(){
  for(const value of [PUBLIC_GATEWAY_URL,PUBLIC_SUPPORT_URL,PUBLIC_API_URL]) if(!validHttpsUrl(value))throw new Error('PUBLIC_ENDPOINT_NOT_CONFIGURED');
  const now=new Date(),expires=new Date(now.getTime()+86400000);
  const payload={type:'NEXO_BOOTSTRAP',format_version:1,config_version:BOOTSTRAP_VERSION,gateway_url:PUBLIC_GATEWAY_URL,support_url:PUBLIC_SUPPORT_URL,api_url:PUBLIC_API_URL,issued_at:now.toISOString(),expires_at:expires.toISOString(),minimum_tls:'1.2',transport:'HTTPS',communication_hub:'/v1/hub/protocol',compatibility_id:COMPATIBILITY_ID};
  return {payload,compact:signPayload(payload),public_jwk:publicJwk(),algorithm:'ES256'};
}

const communicationHub=createCommunicationHub({privateKeyB64:PRIVATE_KEY_B64,publicJwk:publicJwk(),compatibilityId:COMPATIBILITY_ID});

const labEnrollChallenges=new Map();
const labDevices=new Map();
const labActionChallenges=new Map();

function labOnly(res,requestId){
  if(MOBILE_LAB_ENABLED)return true;
  json(res,404,{ok:false,error:'NOT_FOUND',request_id:requestId});
  return false;
}
function labCleanup(){
  const now=Date.now();
  for(const [k,v] of labEnrollChallenges) if(now>Date.parse(v.expiresAt)) labEnrollChallenges.delete(k);
  for(const [k,v] of labActionChallenges) if(now>Date.parse(v.expiresAt)||v.status!=='PENDING') labActionChallenges.delete(k);
}
function verifyP256Jwk(jwk,payload,signature){
  try{
    if(jwk?.kty!=='EC'||jwk?.crv!=='P-256')return false;
    const key=crypto.createPublicKey({key:jwk,format:'jwk'});
    return crypto.verify('sha256',Buffer.from(canonical(payload),'utf8'),{key,dsaEncoding:'ieee-p1363'},Buffer.from(signature,'base64url'));
  }catch{return false}
}
function createLabEnrollChallenge(){
  labCleanup();
  const challengeId=crypto.randomUUID(),deviceId='ios-'+crypto.randomUUID();
  const now=new Date(),expires=new Date(now.getTime()+120000);
  const item={schema:'NEXO_MOBILE_ENROLL_CHALLENGE_V1',challengeId,deviceId,nonce:crypto.randomBytes(24).toString('base64url'),issuedAt:now.toISOString(),expiresAt:expires.toISOString()};
  labEnrollChallenges.set(challengeId,item);return item;
}
function createLabActionChallenge(deviceId,action='APPROVE'){
  labCleanup();
  const device=labDevices.get(deviceId);
  if(!device||device.status!=='ACTIVE')return null;
  const challengeId=crypto.randomUUID(),now=new Date(),expires=new Date(now.getTime()+120000);
  const item={schema:'NEXO_MOBILE_DECISION_V1',ticketId:'NEXO-IOS-FIELD-001',challengeId,correlationId:crypto.randomUUID(),deviceId,audience:'NEXO_MASTER',action,risk:'MEDIUM',nonce:crypto.randomBytes(24).toString('base64url'),issuedAt:now.toISOString(),expiresAt:expires.toISOString(),payloadHash:crypto.createHash('sha256').update('NEXO-IOS-FIELD-001:LAB').digest('hex'),status:'PENDING'};
  labActionChallenges.set(challengeId,item);return {...item};
}

const rate=new Map();
function clientIp(req){return String(req.headers['x-forwarded-for']||req.socket.remoteAddress||'unknown').split(',')[0].trim()}
function allowed(req){
  const now=Date.now(),ip=clientIp(req),current=rate.get(ip);
  if(!current||now-current.start>=RATE_LIMIT_WINDOW_MS){rate.set(ip,{start:now,count:1});return true}
  current.count++; if(rate.size>20000)for(const [key,value] of rate)if(now-value.start>=RATE_LIMIT_WINDOW_MS)rate.delete(key);
  return current.count<=RATE_LIMIT_MAX;
}
async function readJson(req){
  const chunks=[];let size=0;
  for await(const chunk of req){size+=chunk.length;if(size>MAX_BODY_BYTES)throw Object.assign(new Error('BODY_TOO_LARGE'),{status:413});chunks.push(chunk)}
  if(!chunks.length)return {};
  try{return JSON.parse(Buffer.concat(chunks).toString('utf8'))}catch{throw Object.assign(new Error('INVALID_JSON'),{status:400})}
}
async function proxyGateway(body,requestId){
  if(!UPSTREAM_URL)return {status:503,body:{ok:false,error:'CENTRAL_UPSTREAM_NOT_CONFIGURED'}};
  if(!validHttpsUrl(UPSTREAM_URL))return {status:503,body:{ok:false,error:'CENTRAL_UPSTREAM_REJECTED'}};
  const controller=new AbortController(),timer=setTimeout(()=>controller.abort(),12000);
  try{
    const response=await fetch(UPSTREAM_URL,{method:'POST',headers:{'content-type':'application/json','accept':'application/json','x-nexo-request-id':requestId},body:JSON.stringify(body),signal:controller.signal,redirect:'error'});
    const text=await response.text();let parsed;try{parsed=text?JSON.parse(text):{}}catch{return {status:502,body:{ok:false,error:'CENTRAL_INVALID_RESPONSE'}}}
    return {status:response.status,body:parsed};
  }catch(error){return {status:502,body:{ok:false,error:error?.name==='AbortError'?'CENTRAL_TIMEOUT':'CENTRAL_UNREACHABLE'}}}
  finally{clearTimeout(timer)}
}

const MIME={'.html':'text/html; charset=utf-8','.css':'text/css; charset=utf-8','.js':'text/javascript; charset=utf-8','.mjs':'text/javascript; charset=utf-8','.json':'application/json; charset=utf-8','.webmanifest':'application/manifest+json; charset=utf-8','.svg':'image/svg+xml','.png':'image/png','.ico':'image/x-icon'};
async function serveStaticRoot(res,urlPath,prefix,rootDir){
  const relative=urlPath===prefix||urlPath===prefix+'/'?'index.html':urlPath.slice((prefix+'/').length);
  const normalized=path.posix.normalize('/'+relative).slice(1);
  if(normalized.startsWith('..'))return false;
  const full=path.join(rootDir,...normalized.split('/'));
  const root=path.resolve(rootDir)+path.sep;
  const resolved=path.resolve(full);
  if(!resolved.startsWith(root)&&resolved!==path.resolve(rootDir))return false;
  try{
    const data=await fs.readFile(resolved);
    const ext=path.extname(resolved).toLowerCase();
    res.writeHead(200,{
      'content-type':MIME[ext]||'application/octet-stream','content-length':data.length,
      'cache-control':ext==='.html'?'no-cache':'public, max-age=300','x-content-type-options':'nosniff','referrer-policy':'no-referrer',
      'content-security-policy':"default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self' https:; object-src 'none'; base-uri 'self'; frame-ancestors 'none'"
    });res.end(data);return true;
  }catch{return false}
}
async function serveMobile(res,urlPath){
  const relative=urlPath==='/mobile'||urlPath==='/mobile/'?'index.html':urlPath.slice('/mobile/'.length);
  const normalized=path.posix.normalize('/'+relative).slice(1);
  if(normalized.startsWith('..'))return false;
  const full=path.join(MOBILE_ROOT,...normalized.split('/'));
  const root=path.resolve(MOBILE_ROOT)+path.sep;
  const resolved=path.resolve(full);
  if(!resolved.startsWith(root)&&resolved!==path.resolve(MOBILE_ROOT))return false;
  try{
    const data=await fs.readFile(resolved);
    const ext=path.extname(resolved).toLowerCase();
    res.writeHead(200,{
      'content-type':MIME[ext]||'application/octet-stream',
      'content-length':data.length,
      'cache-control':ext==='.html'?'no-cache':'public, max-age=300',
      'x-content-type-options':'nosniff','referrer-policy':'no-referrer',
      'content-security-policy':"default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self' https:; object-src 'none'; base-uri 'self'; frame-ancestors 'none'"
    });res.end(data);return true;
  }catch{return false}
}

const server=http.createServer(async(req,res)=>{
  const requestId=String(req.headers['x-nexo-request-id']||crypto.randomUUID()).slice(0,128);
  res.setHeader('x-nexo-request-id',requestId);
  if(req.method==='OPTIONS'){res.writeHead(204,{'access-control-allow-origin':'*','access-control-allow-methods':'GET,POST,OPTIONS','access-control-allow-headers':'content-type,x-nexo-request-id','access-control-max-age':'600'});return res.end()}
  if(!allowed(req))return json(res,429,{ok:false,error:'RATE_LIMITED',request_id:requestId});
  try{
    const url=new URL(req.url||'/','http://local');
    if(req.method==='GET'&&url.pathname==='/mobile'){res.writeHead(308,{location:'/mobile/'});return res.end()}
    if(req.method==='GET'&&url.pathname.startsWith('/mobile/')){if(await serveMobile(res,url.pathname))return}
    if(req.method==='GET'&&url.pathname==='/pdv-mobile'){res.writeHead(308,{location:'/pdv-mobile/'});return res.end()}
    if(req.method==='GET'&&url.pathname.startsWith('/pdv-mobile/')){if(await serveStaticRoot(res,url.pathname,'/pdv-mobile',PDV_MOBILE_ROOT))return}
    if(req.method==='GET'&&url.pathname==='/health')return json(res,200,{ok:true,service:'NEXO Gateway',version:'0.4.0-hub-lab',bootstrap_configured:Boolean(PRIVATE_KEY_B64&&publicJwk()),upstream_configured:Boolean(UPSTREAM_URL),communication_hub_configured:communicationHub.configured(),communication_hub:'/v1/hub/protocol',compatibility_id:COMPATIBILITY_ID,mobile_path:'/mobile/',pdv_mobile_path:'/pdv-mobile/',mobile_lab_enabled:MOBILE_LAB_ENABLED,time:new Date().toISOString()});
    if(req.method==='GET'&&(url.pathname==='/v1/bootstrap'||url.pathname==='/bootstrap'))return json(res,200,{ok:true,envelope:bootstrapEnvelope(),request_id:requestId});
    if(req.method==='GET'&&url.pathname==='/v1/hub/protocol')return json(res,200,{ok:true,protocol:communicationHub.protocol(),request_id:requestId});
    if(req.method==='GET'&&url.pathname==='/v1/hub/status')return json(res,200,{ok:true,hub:communicationHub.status(),request_id:requestId});
    if(req.method==='POST'&&url.pathname==='/v1/hub/enroll/challenge')return json(res,200,{ok:true,challenge:communicationHub.challenge(await readJson(req)),request_id:requestId});
    if(req.method==='POST'&&url.pathname==='/v1/hub/enroll/complete')return json(res,200,{ok:true,...communicationHub.complete(await readJson(req)),request_id:requestId});
    if(req.method==='POST'&&url.pathname==='/v1/hub/send')return json(res,200,{ok:true,...communicationHub.send(await readJson(req)),request_id:requestId});
    if(req.method==='POST'&&url.pathname==='/v1/hub/poll')return json(res,200,{ok:true,...communicationHub.poll(await readJson(req)),request_id:requestId});
    if(req.method==='POST'&&url.pathname==='/v1/hub/ack')return json(res,200,{ok:true,...communicationHub.ack(await readJson(req)),request_id:requestId});

    if(req.method==='POST'&&url.pathname==='/v1/mobile/lab/enroll/challenge'){
      if(!labOnly(res,requestId))return;
      return json(res,200,{ok:true,challenge:createLabEnrollChallenge(),request_id:requestId});
    }
    if(req.method==='POST'&&url.pathname==='/v1/mobile/lab/enroll/complete'){
      if(!labOnly(res,requestId))return;
      const body=await readJson(req);
      const ch=labEnrollChallenges.get(String(body.challengeId||''));
      if(!ch)return json(res,400,{ok:false,error:'ENROLL_CHALLENGE_NOT_FOUND',request_id:requestId});
      if(Date.now()>Date.parse(ch.expiresAt)){labEnrollChallenges.delete(ch.challengeId);return json(res,400,{ok:false,error:'ENROLL_CHALLENGE_EXPIRED',request_id:requestId})}
      if(body.deviceId!==ch.deviceId||body.nonce!==ch.nonce)return json(res,400,{ok:false,error:'ENROLL_CHALLENGE_MISMATCH',request_id:requestId});
      const payload={schema:'NEXO_MOBILE_ENROLLMENT_V1',challengeId:ch.challengeId,deviceId:ch.deviceId,nonce:ch.nonce,publicJwk:body.publicJwk};
      if(!verifyP256Jwk(body.publicJwk,payload,String(body.signature||'')))return json(res,400,{ok:false,error:'ENROLL_SIGNATURE_INVALID',request_id:requestId});
      labDevices.set(ch.deviceId,{deviceId:ch.deviceId,publicJwk:body.publicJwk,status:'ACTIVE',enrolledAt:new Date().toISOString()});
      labEnrollChallenges.delete(ch.challengeId);
      return json(res,200,{ok:true,device:{deviceId:ch.deviceId,status:'ACTIVE'},request_id:requestId});
    }
    if(req.method==='POST'&&url.pathname==='/v1/mobile/lab/challenge'){
      if(!labOnly(res,requestId))return;
      const body=await readJson(req),action=String(body.action||'APPROVE').toUpperCase();
      if(!['APPROVE','DENY'].includes(action))return json(res,400,{ok:false,error:'BAD_ACTION',request_id:requestId});
      const challenge=createLabActionChallenge(String(body.deviceId||''),action);
      if(!challenge)return json(res,404,{ok:false,error:'DEVICE_NOT_ENROLLED',request_id:requestId});
      return json(res,200,{ok:true,challenge,request_id:requestId});
    }
    if(req.method==='POST'&&url.pathname==='/v1/mobile/lab/decision'){
      if(!labOnly(res,requestId))return;
      const body=await readJson(req),challenge=labActionChallenges.get(String(body.challengeId||''));
      if(!challenge)return json(res,400,{ok:false,error:'CHALLENGE_NOT_FOUND_OR_REPLAY',request_id:requestId});
      if(Date.now()>Date.parse(challenge.expiresAt)){labActionChallenges.delete(challenge.challengeId);return json(res,400,{ok:false,error:'APPROVAL_EXPIRED',request_id:requestId})}
      const device=labDevices.get(challenge.deviceId);
      if(!device||device.status!=='ACTIVE')return json(res,400,{ok:false,error:'DEVICE_NOT_ENROLLED',request_id:requestId});
      for(const k of ['ticketId','challengeId','correlationId','deviceId','audience','action','risk','nonce','issuedAt','expiresAt','payloadHash']) if(body[k]!==challenge[k]) return json(res,400,{ok:false,error:`CHALLENGE_MISMATCH_${k}`,request_id:requestId});
      const payload={schema:body.schema,ticketId:body.ticketId,challengeId:body.challengeId,correlationId:body.correlationId,deviceId:body.deviceId,audience:body.audience,action:body.action,risk:body.risk,nonce:body.nonce,issuedAt:body.issuedAt,expiresAt:body.expiresAt,payloadHash:body.payloadHash};
      if(!verifyP256Jwk(device.publicJwk,payload,String(body.signature||'')))return json(res,400,{ok:false,error:'DECISION_SIGNATURE_INVALID',request_id:requestId});
      labActionChallenges.delete(challenge.challengeId);
      return json(res,200,{ok:true,ack:{schema:'NEXO_MOBILE_DECISION_ACK_V1',challengeId:body.challengeId,ticketId:body.ticketId,decision:body.action,acceptedAt:new Date().toISOString()},request_id:requestId});
    }
    const gatewayPaths=new Set(['/','/gateway','/v1/gateway','/support','/license']);
    if(req.method==='POST'&&gatewayPaths.has(url.pathname)){
      const body=await readJson(req);
      if(!body||typeof body!=='object'||Array.isArray(body))return json(res,400,{ok:false,error:'INVALID_PAYLOAD',request_id:requestId});
      const action=String(body.action||'').trim();
      if(!action||action.length>80||!/^[a-z0-9_:-]+$/i.test(action))return json(res,400,{ok:false,error:'INVALID_ACTION',request_id:requestId});
      const proxied=await proxyGateway(body,requestId);
      return json(res,proxied.status,{...proxied.body,request_id:proxied.body?.request_id||requestId});
    }
    return json(res,404,{ok:false,error:'NOT_FOUND',request_id:requestId});
  }catch(error){
    const status=Number(error?.status||500);
    const msg=String(error?.message||'');
    const safe=(['BODY_TOO_LARGE','INVALID_JSON','BOOTSTRAP_SIGNING_KEY_NOT_CONFIGURED','BOOTSTRAP_SIGNATURE_INVALID','PUBLIC_ENDPOINT_NOT_CONFIGURED'].includes(msg)||/^HUB_[A-Z0-9_]+$/.test(msg))?msg:'INTERNAL_ERROR';
    return json(res,status,{ok:false,error:safe,request_id:requestId});
  }
});
server.listen(PORT,'0.0.0.0',()=>console.log(JSON.stringify({event:'NEXO_GATEWAY_READY',port:PORT,version:'0.4.0-hub-lab',bootstrap_version:BOOTSTRAP_VERSION,upstream_configured:Boolean(UPSTREAM_URL),communication_hub_configured:communicationHub.configured(),compatibility_id:COMPATIBILITY_ID,mobile_path:'/mobile/',mobile_lab_enabled:MOBILE_LAB_ENABLED})));
