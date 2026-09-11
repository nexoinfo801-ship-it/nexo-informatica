import http from 'node:http';
import crypto from 'node:crypto';
import { URL } from 'node:url';
import { buildCentral } from './src/core.mjs';
import { MemoryStore } from './src/store-memory.mjs';
import { createPostgresStore } from './src/store-postgres.mjs';

const PORT = Number(process.env.PORT);
if (!Number.isInteger(PORT) || PORT < 1 || PORT > 65535) throw new Error('PORT_REQUIRED');
const MAX_BODY_BYTES = Math.max(16_384, Number(process.env.MAX_BODY_BYTES || 262_144));
const DATABASE_URL = String(process.env.DATABASE_URL || '').trim();
const ALLOW_MEMORY_STORE = String(process.env.ALLOW_MEMORY_STORE || '') === '1';
const GATEWAY_SHARED_SECRET = String(process.env.GATEWAY_SHARED_SECRET || '');
const ALLOW_UNSIGNED_GATEWAY = String(process.env.ALLOW_UNSIGNED_GATEWAY || '') === '1';

let store;
if (DATABASE_URL) store = await createPostgresStore(DATABASE_URL);
else if (ALLOW_MEMORY_STORE) store = new MemoryStore();
else throw new Error('DATABASE_URL_REQUIRED');

const handle = buildCentral({
  store,
  config: {
    publicGatewayUrl: process.env.PUBLIC_GATEWAY_URL,
    licenseStatusKid: process.env.LICENSE_STATUS_KID,
    licenseStatusPrivatePemB64: process.env.LICENSE_STATUS_SIGNING_PRIVATE_KEY_PEM_B64,
    checkIntervalSeconds: process.env.LICENSE_CHECK_INTERVAL_SECONDS || 60,
  },
});

function writeJson(res, status, body, requestId) {
  const raw = JSON.stringify({ ...body, request_id: body?.request_id || requestId });
  res.writeHead(status, {
    'content-type':'application/json; charset=utf-8', 'content-length':Buffer.byteLength(raw), 'cache-control':'no-store',
    'x-content-type-options':'nosniff','x-frame-options':'DENY','referrer-policy':'no-referrer',
    'permissions-policy':'camera=(), microphone=(), geolocation=()', 'content-security-policy':"default-src 'none'; frame-ancestors 'none'",
    'x-nexo-request-id':requestId,
  });
  res.end(raw);
}

async function readJson(req) {
  const chunks=[]; let size=0;
  for await (const chunk of req) {
    size += chunk.length;
    if (size > MAX_BODY_BYTES) { const e=new Error('BODY_TOO_LARGE'); e.status=413; throw e; }
    chunks.push(chunk);
  }
  try { return chunks.length ? JSON.parse(Buffer.concat(chunks).toString('utf8')) : {}; }
  catch { const e=new Error('INVALID_JSON'); e.status=400; throw e; }
}

const server = http.createServer(async (req,res) => {
  const requestId = String(req.headers['x-nexo-request-id'] || crypto.randomUUID()).slice(0,128);
  try {
    const u = new URL(req.url || '/', 'http://local');
    if (req.method === 'GET' && u.pathname === '/health') {
      let db=false; try { db=await store.ping(); } catch {}
      return writeJson(res, db?200:503, { ok:db, service:'NEXO Central', version:'0.1.0-prep', database_ready:db, license_signer_configured:Boolean(process.env.LICENSE_STATUS_SIGNING_PRIVATE_KEY_PEM_B64), gateway_auth_configured:Boolean(GATEWAY_SHARED_SECRET), time:new Date().toISOString() }, requestId);
    }
    if (req.method === 'GET' && u.pathname === '/ready') {
      let db=false; try { db=await store.ping(); } catch {}
      return writeJson(res, db?200:503, { ok:db, ready:db }, requestId);
    }
    if (req.method === 'POST' && (u.pathname === '/' || u.pathname === '/v1/actions' || u.pathname === '/api/v1/gateway')) {
      if (!GATEWAY_SHARED_SECRET && !ALLOW_UNSIGNED_GATEWAY) { const e=new Error('GATEWAY_AUTH_NOT_CONFIGURED'); e.status=503; throw e; }
      if (GATEWAY_SHARED_SECRET && String(req.headers['x-nexo-gateway-auth'] || '') !== GATEWAY_SHARED_SECRET) { const e=new Error('GATEWAY_AUTH_INVALID'); e.status=401; throw e; }
      const body=await readJson(req);
      const out=await handle(body,requestId);
      return writeJson(res,out.status,out.body,requestId);
    }
    return writeJson(res,404,{ok:false,error:'NOT_FOUND'},requestId);
  } catch(e) {
    const safe = new Set(['GATEWAY_AUTH_NOT_CONFIGURED','GATEWAY_AUTH_INVALID','BODY_TOO_LARGE','INVALID_JSON','ACTION_INVALID','PRODUCT_NOT_ALLOWED','INSTALL_ID_INVALID','LICENSE_KEY_INVALID','AUTH_INVALID_LICENSE_OR_INSTALLATION','TICKET_NOT_FOUND','SERIAL_MISMATCH','LICENSE_STATUS_SIGNER_NOT_CONFIGURED','ACTION_NOT_SUPPORTED','REQUEST_ID_REUSED','SUBJECT_INVALID','DESCRIPTION_INVALID','MESSAGE_INVALID']);
    const code=safe.has(e?.message)?e.message:'INTERNAL_ERROR';
    return writeJson(res,Number(e?.status||500),{ok:false,error:code},requestId);
  }
});

server.listen(PORT,'0.0.0.0',()=>console.log(JSON.stringify({event:'NEXO_CENTRAL_READY',port:PORT,database:DATABASE_URL?'postgres':'memory',license_signer_configured:Boolean(process.env.LICENSE_STATUS_SIGNING_PRIVATE_KEY_PEM_B64),gateway_auth_configured:Boolean(GATEWAY_SHARED_SECRET)})));

for (const sig of ['SIGTERM','SIGINT']) process.on(sig, async()=>{ try { await store.close(); } finally { server.close(()=>process.exit(0)); } });
