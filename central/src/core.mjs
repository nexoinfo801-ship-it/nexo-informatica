import { sha256Hex, signCompact, decodePemB64 } from './crypto.mjs';
import * as V from './validation.mjs';

function publicTicket(t) {
  return {
    id: t.id, protocol: t.protocol, local_protocol: t.local_protocol || '', category: t.category, priority: t.priority,
    subject: t.subject, description: t.description || '', status: t.status, owner: t.owner || '', notifications: t.notifications || {},
    messages: Array.isArray(t.messages) ? t.messages.map((m) => ({ id:m.id, author_type:m.author_type, text:m.text, at:m.at || m.created_at, request_id:m.request_id })) : [],
    created_at: t.created_at, updated_at: t.updated_at, closed_at: t.closed_at || null,
  };
}

function sanitizeDiagnostic(value, depth = 0) {
  if (depth > 5) return '[TRUNCATED]';
  if (value === null || value === undefined) return value;
  if (Array.isArray(value)) return value.slice(0, 50).map((v) => sanitizeDiagnostic(v, depth + 1));
  if (typeof value !== 'object') return typeof value === 'string' ? value.slice(0, 1000) : value;
  const out = {};
  for (const [k, v] of Object.entries(value).slice(0, 100)) {
    if (/(password|senha|secret|token|authorization|cookie|license_key|private[_-]?key|cpf|cnpj)/i.test(k)) out[k] = '[REDACTED]';
    else out[k] = sanitizeDiagnostic(v, depth + 1);
  }
  return out;
}

function validHttps(value) {
  try { const u = new URL(String(value || '')); return u.protocol === 'https:' && !u.username && !u.password; } catch { return false; }
}

async function authenticated(body, store) {
  V.safeProduct(body.product);
  const iid = V.installId(body.install_id);
  const key = V.licenseKey(body.license_key);
  const auth = await store.authenticate({ licenseKeyHash: sha256Hex(key), installId: iid });
  if (!auth) throw V.bad('AUTH_INVALID_LICENSE_OR_INSTALLATION', 401);
  return auth;
}

function sameIdempotencyScope(previous, auth) {
  return String(previous?.company_id || '') === String(auth?.company_id || '')
    && String(previous?.license_id || '') === String(auth?.license_id || '')
    && String(previous?.install_id || '') === String(auth?.install_id || '');
}

function licensePayload(auth, config) {
  const st = V.status(auth.status || auth.license_status || 'ACTIVE');
  const blocking = st !== 'ACTIVE';
  const scope = st === 'CLIENT_BLOCKED' ? 'CLIENT' : ['ACTIVATION_BLOCKED','TRANSFERRED'].includes(st) ? 'ACTIVATION' : blocking ? 'LICENSE' : null;
  const now = new Date().toISOString();
  const p = {
    type: 'NEXO_LICENSE_STATUS', format_version: 3, key_id: config.licenseStatusKid,
    serial: String(auth.serial || ''), license_id: String(auth.license_id || ''), activation_id: String(auth.activation_id || ''), install_id: String(auth.install_id || ''),
    status_seq: Math.max(1, Number(auth.status_seq || 1)), status: st, revoked: blocking, block_scope: scope, block_reason: blocking ? (config.blockReason || st) : '',
    message: blocking ? (config.blockReason || st) : 'Ativa no servidor', issued_at: now, server_time: now, offline_until: null,
    check_interval_seconds: Math.max(30, Math.min(300, Number(config.checkIntervalSeconds || 60))),
  };
  if (validHttps(config.publicGatewayUrl)) p.gateway_url = config.publicGatewayUrl;
  return p;
}

export function buildCentral({ store, config = {} }) {
  if (!store) throw new Error('STORE_REQUIRED');
  const cfg = {
    publicGatewayUrl: String(config.publicGatewayUrl || ''),
    licenseStatusKid: String(config.licenseStatusKid || 'lic-status-unconfigured'),
    licenseStatusPrivatePemB64: String(config.licenseStatusPrivatePemB64 || ''),
    checkIntervalSeconds: Number(config.checkIntervalSeconds || 60),
    blockReason: String(config.blockReason || ''),
  };

  return async function handle(body, requestId) {
    const action = V.action(body?.action);
    const rid = V.text(requestId, { min: 8, max: 128, field: 'request_id' });

    if (action === 'health') return { status: 200, body: { ok:true, service:'NEXO Central', version:'0.1.0-prep', database:'configured', time:new Date().toISOString() } };

    // Authentication happens before idempotency lookup. A known request_id must never
    // become a bearer credential capable of replaying another installation's response.
    const auth = await authenticated(body, store);
    const previous = await store.getIdempotent(rid);
    if (previous) {
      if (previous.action !== action || !sameIdempotencyScope(previous, auth)) throw V.bad('REQUEST_ID_REUSED', 409);
      return { status: 200, body: previous.response };
    }

    let response;

    if (action === 'support_create') {
      const ticket = body.ticket && typeof body.ticket === 'object' ? body.ticket : {};
      const input = {
        local_protocol: V.text(ticket.local_protocol || '', { max: 80, field:'local_protocol' }),
        category: V.category(ticket.category), priority: V.priority(ticket.priority),
        subject: V.text(ticket.subject, { min: 2, max: 180, field:'subject' }),
        description: V.text(ticket.description, { min: 2, max: 8000, field:'description' }),
        diagnostic: body.diagnostic && typeof body.diagnostic === 'object' && !Array.isArray(body.diagnostic) ? sanitizeDiagnostic(body.diagnostic) : {},
      };
      const created = await store.createTicket(auth, input);
      response = { ok:true, ticket:publicTicket(created) };
      await store.addAudit({ company_id:auth.company_id, install_id:auth.install_id, action, entity_type:'ticket', entity_id:created.id, request_id:rid, outcome:'OK', meta:{ protocol:created.protocol } });
    } else if (action === 'support_status' || action === 'support_sync') {
      const tickets = await store.listTickets(auth);
      response = { ok:true, tickets:tickets.map(publicTicket) };
    } else if (action === 'support_message') {
      const tid = V.text(body.ticket_id, { min:8, max:80, field:'ticket_id' });
      const msg = V.text(body.message, { min:1, max:8000, field:'message' });
      const updated = await store.addMessage(auth, tid, msg, rid, 'client');
      if (!updated) throw V.bad('TICKET_NOT_FOUND', 404);
      response = { ok:true, ticket:publicTicket(updated) };
      await store.addAudit({ company_id:auth.company_id, install_id:auth.install_id, action, entity_type:'ticket', entity_id:tid, request_id:rid, outcome:'OK' });
    } else if (action === 'support_client_close') {
      const tid = V.text(body.ticket_id, { min:8, max:80, field:'ticket_id' });
      const updated = await store.closeTicket(auth, tid);
      if (!updated) throw V.bad('TICKET_NOT_FOUND', 404);
      response = { ok:true, ticket:publicTicket(updated) };
      await store.addAudit({ company_id:auth.company_id, install_id:auth.install_id, action, entity_type:'ticket', entity_id:tid, request_id:rid, outcome:'OK' });
    } else if (action === 'license_ack') {
      const state = V.text(body.state, { min:2, max:40, field:'state' });
      await store.addLicenseAck(auth, state, body.client_time ? V.text(body.client_time,{max:64,field:'client_time'}) : '', rid);
      response = { ok:true, acknowledged:true };
    } else if (action === 'license_status') {
      if (body.serial && String(body.serial) !== String(auth.serial)) throw V.bad('SERIAL_MISMATCH', 403);
      if (!cfg.licenseStatusPrivatePemB64 || !cfg.licenseStatusKid.startsWith('lic-status-')) throw V.bad('LICENSE_STATUS_SIGNER_NOT_CONFIGURED', 503);
      const payload = licensePayload(auth, cfg);
      const pem = decodePemB64(cfg.licenseStatusPrivatePemB64);
      response = { ok:true, status_envelope:signCompact(payload, pem) };
      await store.addAudit({ company_id:auth.company_id, install_id:auth.install_id, action, entity_type:'license', entity_id:auth.license_id, request_id:rid, outcome:'OK', meta:{ status:payload.status, seq:payload.status_seq } });
    } else {
      throw V.bad('ACTION_NOT_SUPPORTED', 400);
    }

    await store.putIdempotent(rid, action, auth, response);
    return { status:200, body:response };
  };
}
