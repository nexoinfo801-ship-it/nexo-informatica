import { uuid } from './crypto.mjs';

export async function createPostgresStore(connectionString) {
  const { Pool } = await import('pg');
  const pool = new Pool({ connectionString, max: Number(process.env.PG_POOL_MAX || 4), idleTimeoutMillis: 10_000, connectionTimeoutMillis: 5_000, ssl: process.env.PG_SSL === 'disable' ? false : { rejectUnauthorized: process.env.PG_SSL_REJECT_UNAUTHORIZED !== 'false' } });

  const ticketShape = async (client, row) => {
    const { rows: messages } = await client.query('SELECT id, author_type, text, created_at AS at, request_id FROM nexo_support_messages WHERE ticket_id=$1 ORDER BY created_at,id', [row.id]);
    return { ...row, messages };
  };

  return {
    async ping() { const c = await pool.connect(); try { await c.query('SELECT 1'); return true; } finally { c.release(); } },
    async close() { await pool.end(); },
    async authenticate({ licenseKeyHash, installId }) {
      const q = `SELECT l.company_id,l.id AS license_id,l.serial,l.status AS license_status,a.id AS activation_id,a.install_id,a.status,a.status_seq
                 FROM nexo_licenses l JOIN nexo_activations a ON a.license_id=l.id
                WHERE l.license_key_hash=$1 AND a.install_id=$2 LIMIT 1`;
      const { rows } = await pool.query(q, [licenseKeyHash, installId]);
      return rows[0] || null;
    },
    async getIdempotent(requestId) {
      const { rows } = await pool.query('SELECT response_json AS response, action, company_id, license_id, install_id FROM nexo_idempotency WHERE request_id=$1 AND expires_at > now()', [requestId]);
      return rows[0] || null;
    },
    async putIdempotent(requestId, action, auth, response) {
      await pool.query(`INSERT INTO nexo_idempotency(request_id,company_id,license_id,install_id,action,response_json,expires_at) VALUES($1,$2,$3,$4,$5,$6::jsonb,now()+interval '24 hours') ON CONFLICT(request_id) DO NOTHING`, [requestId, auth?.company_id || null, auth?.license_id || null, auth?.install_id || null, action, JSON.stringify(response)]);
    },
    async createTicket(auth, input) {
      const id = uuid(), now = new Date(), protocol = `SUP-${now.toISOString().slice(0,10).replaceAll('-','')}-${id.replaceAll('-','').slice(0,6).toUpperCase()}`;
      const { rows } = await pool.query(`INSERT INTO nexo_support_tickets(id,protocol,company_id,license_id,activation_id,install_id,local_protocol,category,priority,subject,description,status,owner,diagnostic,notifications)
        VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,'Aberto','',$12::jsonb,'{}'::jsonb)
        RETURNING id,protocol,local_protocol,category,priority,subject,description,status,owner,notifications,created_at,updated_at`,
        [id, protocol, auth.company_id, auth.license_id, auth.activation_id, auth.install_id, input.local_protocol || '', input.category, input.priority, input.subject, input.description, JSON.stringify(input.diagnostic || {})]);
      return { ...rows[0], messages: [] };
    },
    async listTickets(auth) {
      const c = await pool.connect(); try {
        const { rows } = await c.query(`SELECT id,protocol,local_protocol,category,priority,subject,description,status,owner,notifications,created_at,updated_at,closed_at FROM nexo_support_tickets WHERE company_id=$1 AND license_id=$2 AND install_id=$3 ORDER BY updated_at DESC LIMIT 200`, [auth.company_id, auth.license_id, auth.install_id]);
        const out=[]; for (const row of rows) out.push(await ticketShape(c,row)); return out;
      } finally { c.release(); }
    },
    async getTicket(auth, ticketId) {
      const c = await pool.connect(); try {
        const { rows } = await c.query(`SELECT id,protocol,local_protocol,category,priority,subject,description,status,owner,notifications,created_at,updated_at,closed_at FROM nexo_support_tickets WHERE id=$1 AND company_id=$2 AND license_id=$3 AND install_id=$4`, [ticketId, auth.company_id, auth.license_id, auth.install_id]);
        return rows[0] ? ticketShape(c, rows[0]) : null;
      } finally { c.release(); }
    },
    async addMessage(auth, ticketId, text, requestId, authorType='client') {
      const c = await pool.connect(); try {
        await c.query('BEGIN');
        const own = await c.query('SELECT id FROM nexo_support_tickets WHERE id=$1 AND company_id=$2 AND license_id=$3 AND install_id=$4 FOR UPDATE', [ticketId, auth.company_id, auth.license_id, auth.install_id]);
        if (!own.rowCount) { await c.query('ROLLBACK'); return null; }
        await c.query(`INSERT INTO nexo_support_messages(id,ticket_id,company_id,author_type,text,request_id) VALUES($1,$2,$3,$4,$5,$6) ON CONFLICT(request_id) DO NOTHING`, [uuid(), ticketId, auth.company_id, authorType, text, requestId]);
        await c.query('UPDATE nexo_support_tickets SET updated_at=now() WHERE id=$1', [ticketId]);
        await c.query('COMMIT');
        const { rows } = await c.query(`SELECT id,protocol,local_protocol,category,priority,subject,description,status,owner,notifications,created_at,updated_at,closed_at FROM nexo_support_tickets WHERE id=$1`, [ticketId]);
        return ticketShape(c, rows[0]);
      } catch(e) { try { await c.query('ROLLBACK'); } catch {} throw e; } finally { c.release(); }
    },
    async closeTicket(auth, ticketId) {
      const { rows } = await pool.query(`UPDATE nexo_support_tickets SET status='Fechado',closed_at=COALESCE(closed_at,now()),updated_at=now() WHERE id=$1 AND company_id=$2 AND license_id=$3 AND install_id=$4 RETURNING id,protocol,local_protocol,category,priority,subject,description,status,owner,notifications,created_at,updated_at,closed_at`, [ticketId, auth.company_id, auth.license_id, auth.install_id]);
      if (!rows[0]) return null;
      const c=await pool.connect(); try { return await ticketShape(c, rows[0]); } finally { c.release(); }
    },
    async addLicenseAck(auth,state,clientTime,requestId) { await pool.query(`INSERT INTO nexo_license_acks(company_id,license_id,activation_id,install_id,state,client_time,request_id) VALUES($1,$2,$3,$4,$5,$6,$7) ON CONFLICT(request_id) DO NOTHING`, [auth.company_id,auth.license_id,auth.activation_id,auth.install_id,state,clientTime||null,requestId]); },
    async addAudit(evt) { await pool.query(`INSERT INTO nexo_audit_events(company_id,install_id,action,entity_type,entity_id,request_id,outcome,meta) VALUES($1,$2,$3,$4,$5,$6,$7,$8::jsonb)`, [evt.company_id||null,evt.install_id||null,evt.action,evt.entity_type||null,evt.entity_id||null,evt.request_id||null,evt.outcome||'OK',JSON.stringify(evt.meta||{})]); },
  };
}
