import test from 'node:test';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import { spawn } from 'node:child_process';
import net from 'node:net';

function freePort(){return new Promise((resolve,reject)=>{const s=net.createServer();s.on('error',reject);s.listen(0,'127.0.0.1',()=>{const {port}=s.address();s.close(()=>resolve(port))})})}
const {privateKey,publicKey}=crypto.generateKeyPairSync('ec',{namedCurve:'P-256'});
const privatePem=privateKey.export({type:'pkcs8',format:'pem'});
const jwk=publicKey.export({format:'jwk'});
const port=await freePort(),base=`http://127.0.0.1:${port}`;
const child=spawn(process.execPath,['server.mjs'],{cwd:new URL('..',import.meta.url),env:{...process.env,PORT:String(port),PUBLIC_GATEWAY_URL:'https://gateway.example.test',PUBLIC_SUPPORT_URL:'https://gateway.example.test',PUBLIC_API_URL:'https://gateway.example.test',BOOTSTRAP_SIGNING_PRIVATE_KEY_PEM_B64:Buffer.from(privatePem).toString('base64'),BOOTSTRAP_SIGNING_PUBLIC_JWK:JSON.stringify(jwk)},stdio:['ignore','pipe','pipe']});
await new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(new Error('server timeout')),5000);child.stdout.on('data',d=>{if(String(d).includes('NEXO_GATEWAY_READY')){clearTimeout(timer);resolve()}});child.on('exit',code=>reject(new Error(`server exited ${code}`)))});
test.after(()=>child.kill('SIGTERM'));

test('health is ready and exposes mobile path without secrets',async()=>{const r=await fetch(`${base}/health`);assert.equal(r.status,200);const data=await r.json();assert.equal(data.ok,true);assert.equal(data.bootstrap_configured,true);assert.equal(data.upstream_configured,false);assert.equal(data.mobile_path,'/mobile/');assert.equal(JSON.stringify(data).includes('PRIVATE KEY'),false)});
test('mobile PWA is served by gateway',async()=>{const r=await fetch(`${base}/mobile/`);assert.equal(r.status,200);const html=await r.text();assert.match(html,/NEXA Mobile/);assert.match(r.headers.get('content-type'),/text\/html/)});
test('mobile manifest is served',async()=>{const r=await fetch(`${base}/mobile/manifest.webmanifest`);assert.equal(r.status,200);const m=await r.json();assert.equal(m.short_name,'NEXA')});
test('bootstrap envelope is signed with P-256',async()=>{const r=await fetch(`${base}/v1/bootstrap`);assert.equal(r.status,200);const data=await r.json();const [p,s,extra]=String(data.envelope.compact).split('.');assert.ok(p&&s&&!extra);const signature=Buffer.from(s,'base64url');assert.equal(signature.length,64);assert.equal(crypto.verify('sha256',Buffer.from(p),{key:publicKey,dsaEncoding:'ieee-p1363'},signature),true);const payload=JSON.parse(Buffer.from(p,'base64url').toString('utf8'));assert.equal(payload.gateway_url,'https://gateway.example.test')});
test('gateway fails closed until Central upstream exists',async()=>{const r=await fetch(`${base}/v1/gateway`,{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({action:'support_status'})});assert.equal(r.status,503);const data=await r.json();assert.equal(data.error,'CENTRAL_UPSTREAM_NOT_CONFIGURED')});
test('invalid gateway action is rejected',async()=>{const r=await fetch(`${base}/v1/gateway`,{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({action:'../../bad'})});assert.equal(r.status,400);const data=await r.json();assert.equal(data.error,'INVALID_ACTION')});
