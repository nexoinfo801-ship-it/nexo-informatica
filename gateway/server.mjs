import http from 'node:http';
import crypto from 'node:crypto';
import { URL } from 'node:url';

const PORT = Number(process.env.PORT || 3000);
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

function json(res, status, body, extra = {}) {
  const raw = JSON.stringify(body);
  res.writeHead(status, {
    'content-type': 'application/json; charset=utf-8',
    'content-length': Buffer.byteLength(raw),
    'cache-control': 'no-store',
    'x-content-type-options': 'nosniff',
    'x-frame-options': 'DENY',
    'referrer-policy': 'no-referrer',
    'permissions-policy': 'camera=(), microphone=(), geolocation=()',
    'content-security-policy': "default-src 'none'; frame-ancestors 'none'",
    'access-control-allow-origin': '*',
    'access-control-allow-methods': 'GET,POST,OPTIONS',
    'access-control-allow-headers': 'content-type,x-nexo-request-id',
    ...extra,
  });
  res.end(raw);
}

function stable(value) {
  if (Array.isArray(value)) return value.map(stable);
  if (value && typeof value === 'object') {
    return Object.fromEntries(Object.keys(value).sort().map((k) => [k, stable(value[k])]));
  }
  return value;
}

function publicJwk() {
  if (!PUBLIC_JWK_RAW) return null;
  try { return JSON.parse(PUBLIC_JWK_RAW); } catch { return null; }
}

function signPayload(payload) {
  if (!PRIVATE_KEY_B64) throw new Error('BOOTSTRAP_SIGNING_KEY_NOT_CONFIGURED');
  const pem = Buffer.from(PRIVATE_KEY_B64, 'base64').toString('utf8');
  const canonical = JSON.stringify(stable(payload));
  const p64 = Buffer.from(canonical, 'utf8').toString('base64url');
  const signature = crypto.sign('sha256', Buffer.from(p64, 'utf8'), {
    key: pem,
    dsaEncoding: 'ieee-p1363',
  });
  if (signature.length !== 64) throw new Error('BOOTSTRAP_SIGNATURE_INVALID');
  return `${p64}.${signature.toString('base64url')}`;
}

function validHttpsUrl(value) {
  try {
    const u = new URL(value);
    return u.protocol === 'https:' && !u.username && !u.password;
  } catch { return false; }
}

function bootstrapEnvelope() {
  for (const value of [PUBLIC_GATEWAY_URL, PUBLIC_SUPPORT_URL, PUBLIC_API_URL]) {
    if (!validHttpsUrl(value)) throw new Error('PUBLIC_ENDPOINT_NOT_CONFIGURED');
  }
  const now = new Date();
  const expires = new Date(now.getTime() + 24 * 60 * 60 * 1000);
  const payload = {
    type: 'NEXO_BOOTSTRAP',
    format_version: 1,
    config_version: BOOTSTRAP_VERSION,
    gateway_url: PUBLIC_GATEWAY_URL,
    support_url: PUBLIC_SUPPORT_URL,
    api_url: PUBLIC_API_URL,
    issued_at: now.toISOString(),
    expires_at: expires.toISOString(),
    minimum_tls: '1.2',
    transport: 'HTTPS',
  };
  return {
    payload,
    compact: signPayload(payload),
    public_jwk: publicJwk(),
    algorithm: 'ES256',
  };
}

const rate = new Map();
function clientIp(req) {
  return String(req.headers['x-forwarded-for'] || req.socket.remoteAddress || 'unknown').split(',')[0].trim();
}
function allowed(req) {
  const now = Date.now();
  const ip = clientIp(req);
  const current = rate.get(ip);
  if (!current || now - current.start >= RATE_LIMIT_WINDOW_MS) {
    rate.set(ip, { start: now, count: 1 });
    return true;
  }
  current.count += 1;
  if (rate.size > 20_000) {
    for (const [key, value] of rate) if (now - value.start >= RATE_LIMIT_WINDOW_MS) rate.delete(key);
  }
  return current.count <= RATE_LIMIT_MAX;
}

async function readJson(req) {
  const chunks = [];
  let size = 0;
  for await (const chunk of req) {
    size += chunk.length;
    if (size > MAX_BODY_BYTES) throw Object.assign(new Error('BODY_TOO_LARGE'), { status: 413 });
    chunks.push(chunk);
  }
  if (!chunks.length) return {};
  try { return JSON.parse(Buffer.concat(chunks).toString('utf8')); }
  catch { throw Object.assign(new Error('INVALID_JSON'), { status: 400 }); }
}

async function proxyGateway(body, requestId) {
  if (!UPSTREAM_URL) return { status: 503, body: { ok: false, error: 'CENTRAL_UPSTREAM_NOT_CONFIGURED' } };
  if (!validHttpsUrl(UPSTREAM_URL)) return { status: 503, body: { ok: false, error: 'CENTRAL_UPSTREAM_REJECTED' } };

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 12_000);
  try {
    const response = await fetch(UPSTREAM_URL, {
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        'accept': 'application/json',
        'x-nexo-request-id': requestId,
      },
      body: JSON.stringify(body),
      signal: controller.signal,
      redirect: 'error',
    });
    const text = await response.text();
    let parsed;
    try { parsed = text ? JSON.parse(text) : {}; }
    catch { return { status: 502, body: { ok: false, error: 'CENTRAL_INVALID_RESPONSE' } }; }
    return { status: response.status, body: parsed };
  } catch (error) {
    return { status: 502, body: { ok: false, error: error?.name === 'AbortError' ? 'CENTRAL_TIMEOUT' : 'CENTRAL_UNREACHABLE' } };
  } finally {
    clearTimeout(timer);
  }
}

const server = http.createServer(async (req, res) => {
  const requestId = String(req.headers['x-nexo-request-id'] || crypto.randomUUID()).slice(0, 128);
  res.setHeader('x-nexo-request-id', requestId);

  if (req.method === 'OPTIONS') {
    res.writeHead(204, {
      'access-control-allow-origin': '*',
      'access-control-allow-methods': 'GET,POST,OPTIONS',
      'access-control-allow-headers': 'content-type,x-nexo-request-id',
      'access-control-max-age': '600',
    });
    return res.end();
  }

  if (!allowed(req)) return json(res, 429, { ok: false, error: 'RATE_LIMITED', request_id: requestId });

  try {
    const url = new URL(req.url || '/', 'http://local');

    if (req.method === 'GET' && url.pathname === '/health') {
      return json(res, 200, {
        ok: true,
        service: 'NEXO Gateway',
        version: '0.1.0-prep',
        bootstrap_configured: Boolean(PRIVATE_KEY_B64 && publicJwk()),
        upstream_configured: Boolean(UPSTREAM_URL),
        time: new Date().toISOString(),
      });
    }

    if (req.method === 'GET' && (url.pathname === '/v1/bootstrap' || url.pathname === '/bootstrap')) {
      return json(res, 200, { ok: true, envelope: bootstrapEnvelope(), request_id: requestId });
    }

    const gatewayPaths = new Set(['/', '/gateway', '/v1/gateway', '/support', '/license']);
    if (req.method === 'POST' && gatewayPaths.has(url.pathname)) {
      const body = await readJson(req);
      if (!body || typeof body !== 'object' || Array.isArray(body)) {
        return json(res, 400, { ok: false, error: 'INVALID_PAYLOAD', request_id: requestId });
      }
      const action = String(body.action || '').trim();
      if (!action || action.length > 80 || !/^[a-z0-9_:-]+$/i.test(action)) {
        return json(res, 400, { ok: false, error: 'INVALID_ACTION', request_id: requestId });
      }
      const proxied = await proxyGateway(body, requestId);
      return json(res, proxied.status, { ...proxied.body, request_id: proxied.body?.request_id || requestId });
    }

    return json(res, 404, { ok: false, error: 'NOT_FOUND', request_id: requestId });
  } catch (error) {
    const status = Number(error?.status || 500);
    const safe = ['BODY_TOO_LARGE', 'INVALID_JSON', 'BOOTSTRAP_SIGNING_KEY_NOT_CONFIGURED', 'BOOTSTRAP_SIGNATURE_INVALID', 'PUBLIC_ENDPOINT_NOT_CONFIGURED'].includes(error?.message)
      ? error.message
      : 'INTERNAL_ERROR';
    return json(res, status, { ok: false, error: safe, request_id: requestId });
  }
});

server.listen(PORT, '0.0.0.0', () => {
  console.log(JSON.stringify({ event: 'NEXO_GATEWAY_READY', port: PORT, bootstrap_version: BOOTSTRAP_VERSION, upstream_configured: Boolean(UPSTREAM_URL) }));
});
