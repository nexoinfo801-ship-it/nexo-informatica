import { sha256Hex, uuid } from './crypto.mjs';

export class MemoryStore {
  constructor() {
    this.licenses = new Map();
    this.tickets = new Map();
    this.idempotency = new Map();
    this.acks = [];
    this.audit = [];
  }

  seedLicense({ companyId = uuid(), licenseId = uuid(), activationId = uuid(), serial = 'NX-TEST-001', licenseKey, installId, status = 'ACTIVE', statusSeq = 1 }) {
    const rec = { company_id: companyId, license_id: licenseId, activation_id: activationId, serial, license_key_hash: sha256Hex(licenseKey), install_id: installId, status, status_seq: statusSeq };
    this.licenses.set(`${rec.license_key_hash}:${installId}`, rec);
    return rec;
  }

  async authenticate({ licenseKeyHash, installId }) {
    return this.licenses.get(`${licenseKeyHash}:${installId}`) || null;
  }

  async getIdempotent(requestId) { return this.idempotency.get(requestId) || null; }
  async putIdempotent(requestId, action, auth, response) { this.idempotency.set(requestId, { action, company_id: auth?.company_id || null, response }); }

  async createTicket(auth, input) {
    const id = uuid();
    const now = new Date().toISOString();
    const protocol = `SUP-${now.slice(0,10).replaceAll('-','')}-${id.replaceAll('-','').slice(0,6).toUpperCase()}`;
    const t = {
      id, protocol, company_id: auth.company_id, license_id: auth.license_id, activation_id: auth.activation_id,
      install_id: auth.install_id, local_protocol: input.local_protocol || '', category: input.category, priority: input.priority,
      subject: input.subject, description: input.description, status: 'Aberto', owner: '', notifications: {}, diagnostic: input.diagnostic || {},
      messages: [], created_at: now, updated_at: now,
    };
    this.tickets.set(id, t);
    return structuredClone(t);
  }

  async listTickets(auth) {
    return [...this.tickets.values()].filter((t) => t.company_id === auth.company_id && t.license_id === auth.license_id && t.install_id === auth.install_id).map((x) => structuredClone(x));
  }

  async getTicket(auth, ticketId) {
    const t = this.tickets.get(ticketId);
    if (!t || t.company_id !== auth.company_id || t.license_id !== auth.license_id || t.install_id !== auth.install_id) return null;
    return structuredClone(t);
  }

  async addMessage(auth, ticketId, text, requestId, authorType = 'client') {
    const t = this.tickets.get(ticketId);
    if (!t || t.company_id !== auth.company_id || t.license_id !== auth.license_id || t.install_id !== auth.install_id) return null;
    const existing = t.messages.find((m) => m.request_id === requestId);
    if (!existing) t.messages.push({ id: uuid(), author_type: authorType, text, at: new Date().toISOString(), request_id: requestId });
    t.updated_at = new Date().toISOString();
    return structuredClone(t);
  }

  async closeTicket(auth, ticketId) {
    const t = this.tickets.get(ticketId);
    if (!t || t.company_id !== auth.company_id || t.license_id !== auth.license_id || t.install_id !== auth.install_id) return null;
    t.status = 'Fechado';
    t.closed_at = new Date().toISOString();
    t.updated_at = t.closed_at;
    return structuredClone(t);
  }

  async addLicenseAck(auth, state, clientTime, requestId) { this.acks.push({ ...auth, state, client_time: clientTime, request_id: requestId, at: new Date().toISOString() }); }
  async addAudit(evt) { this.audit.push({ ...evt, at: new Date().toISOString() }); }
  async ping() { return true; }
  async close() {}
}
