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
