import pg from 'pg';

const { Pool } = pg;
const connectionString = String(process.env.DATABASE_URL || '').trim();
const pool = connectionString
  ? new Pool({
      connectionString,
      max: Math.max(1, Math.min(10, Number(process.env.DB_POOL_MAX || 4))),
      idleTimeoutMillis: 30000,
      connectionTimeoutMillis: 5000,
      maxUses: 1000,
      ssl: connectionString.includes('sslmode=require') ? { rejectUnauthorized: false } : undefined
    })
  : null;

export function databaseConfigured() {
  return Boolean(pool);
}

export async function databaseHealth() {
  if (!pool) return { configured: false, reachable: false };
  try {
    const result = await pool.query('SELECT 1 AS ok');
    return { configured: true, reachable: result.rows?.[0]?.ok === 1 };
  } catch {
    return { configured: true, reachable: false };
  }
}

export async function databaseQuery(text, values = []) {
  if (!pool) throw Object.assign(new Error('DATABASE_NOT_CONFIGURED'), { status: 503 });
  return pool.query(text, values);
}

export async function closeDatabase() {
  if (pool) await pool.end();
}

export async function listSyncEvents({ companyId, afterSequence = 0, limit = 100 }) {
  const safeLimit = Math.max(1, Math.min(500, Number(limit) || 100));
  const result = await databaseQuery(
    `SELECT server_sequence, event_uuid, company_id, origin_device_id,
            device_sequence, entity_type, entity_id, base_version,
            entity_version, operation, payload_sha256, payload, created_at
       FROM nexo.sync_events
      WHERE company_id = $1 AND server_sequence > $2
      ORDER BY server_sequence ASC
      LIMIT $3`,
    [companyId, Math.max(0, Number(afterSequence) || 0), safeLimit]
  );
  return result.rows;
}

export async function appendSyncEvent(event) {
  const result = await databaseQuery(
    `INSERT INTO nexo.sync_events
      (event_uuid, company_id, origin_device_id, device_sequence,
       entity_type, entity_id, base_version, entity_version,
       operation, payload_sha256, payload)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)
     ON CONFLICT (event_uuid) DO NOTHING
     RETURNING server_sequence, event_uuid, company_id, origin_device_id,
               device_sequence, entity_type, entity_id, base_version,
               entity_version, operation, payload_sha256, payload, created_at`,
    [
      event.eventUuid, event.companyId, event.originDeviceId,
      event.deviceSequence, event.entityType, event.entityId,
      event.baseVersion ?? 0, event.entityVersion, event.operation,
      event.payloadSha256, event.payload ?? null
    ]
  );
  if (result.rows[0]) return { inserted: true, event: result.rows[0] };
  const duplicate = await databaseQuery(
    `SELECT server_sequence, event_uuid, company_id, origin_device_id,
            device_sequence, entity_type, entity_id, base_version,
            entity_version, operation, payload_sha256, payload, created_at
       FROM nexo.sync_events
      WHERE event_uuid = $1`,
    [event.eventUuid]
  );
  return { inserted: false, event: duplicate.rows[0] || null };
}

export async function listSupportTickets({ companyId, limit = 100 }) {
  const safeLimit = Math.max(1, Math.min(200, Number(limit) || 100));
  const result = await databaseQuery(
    `SELECT id, company_id, opened_by_user_id, assigned_to, subject,
            status, priority, payload, created_at, updated_at, closed_at
       FROM nexo.support_tickets
      WHERE company_id = $1
      ORDER BY updated_at DESC
      LIMIT $2`,
    [companyId, safeLimit]
  );
  return result.rows;
}
