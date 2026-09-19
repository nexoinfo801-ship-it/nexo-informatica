function err(code,status=500){return Object.assign(new Error(code),{status});}
function clone(v){return v&&typeof v==='object'?JSON.parse(JSON.stringify(v)):v;}

export class MemoryMobileStore{
  constructor({now=()=>Date.now()}={}){this.now=now;this.pairings=new Map();this.sessions=new Map();this.tickets=new Map();}
  persistence(){return 'MEMORY';}
  async init(){return {ok:true,persistence:this.persistence()};}
  async close(){}
  cleanup(){const t=this.now();for(const[k,v]of this.pairings)if(Date.parse(v.expiresAt)<=t&&!v.usedAt)this.pairings.delete(k);for(const[k,v]of this.sessions)if(Date.parse(v.expiresAt)<=t||v.revokedAt)this.sessions.delete(k);}
  async putPairing(rec){this.cleanup();this.pairings.set(rec.pairingId,clone(rec));return clone(rec);}
  async consumePairing(pairingId,tokenHash){this.cleanup();const rec=this.pairings.get(pairingId);if(!rec)throw err('MOBILE_PAIRING_NOT_FOUND',404);if(rec.usedAt)throw err('MOBILE_PAIRING_USED',409);if(Date.parse(rec.expiresAt)<=this.now())throw err('MOBILE_PAIRING_EXPIRED',410);if(rec.tokenHash!==tokenHash)throw err('MOBILE_PAIRING_TOKEN_INVALID',401);rec.usedAt=new Date(this.now()).toISOString();return clone(rec);}
  async putSession(rec){this.cleanup();this.sessions.set(rec.tokenHash,clone(rec));return clone(rec);}
  async getSession(tokenHash){this.cleanup();const rec=this.sessions.get(tokenHash);return rec?clone(rec):null;}
  async putTicket(rec){this.tickets.set(rec.protocol,clone(rec));return clone(rec);}
  async listTickets(deviceId){return[...this.tickets.values()].filter(x=>x.deviceId===deviceId).sort((a,b)=>String(b.createdAt).localeCompare(String(a.createdAt))).map(clone);}
  snapshot(){return{pairings:[...this.pairings.values()].map(clone),sessions:[...this.sessions.values()].map(clone),tickets:[...this.tickets.values()].map(clone)};}
}

export class PostgresMobileStore{
  constructor({databaseUrl,PoolClass=null}={}){this.databaseUrl=String(databaseUrl||'').trim();if(!this.databaseUrl)throw err('MOBILE_DATABASE_URL_REQUIRED',503);this.PoolClass=PoolClass;this.pool=null;}
  persistence(){return 'POSTGRES';}
  async init(){
    let Pool=this.PoolClass;if(!Pool){({Pool}=await import('pg'));}
    const internal=/\.railway\.internal(?::|\/|$)/i.test(this.databaseUrl),local=/localhost|127\.0\.0\.1/i.test(this.databaseUrl),sslRequired=/sslmode=require/i.test(this.databaseUrl)||(!internal&&!local);
    this.pool=new Pool({connectionString:this.databaseUrl,max:5,idleTimeoutMillis:30000,connectionTimeoutMillis:8000,ssl:sslRequired?{rejectUnauthorized:false}:undefined});
    const c=await this.pool.connect();
    try{
      await c.query(`CREATE TABLE IF NOT EXISTS nexo_mobile_pairings (
        pairing_id VARCHAR(128) PRIMARY KEY,
        token_hash CHAR(64) NOT NULL,
        hub_node_id VARCHAR(128) NOT NULL,
        issuer_node_id VARCHAR(128) NOT NULL,
        scopes JSONB NOT NULL,
        created_at TIMESTAMPTZ NOT NULL,
        expires_at TIMESTAMPTZ NOT NULL,
        used_at TIMESTAMPTZ NULL
      )`);
      await c.query(`CREATE INDEX IF NOT EXISTS nexo_mobile_pairings_expires_idx ON nexo_mobile_pairings(expires_at)`);
      await c.query(`CREATE TABLE IF NOT EXISTS nexo_mobile_sessions (
        token_hash CHAR(64) PRIMARY KEY,
        device_id VARCHAR(128) NOT NULL,
        label VARCHAR(80) NOT NULL,
        hub_node_id VARCHAR(128) NOT NULL,
        scopes JSONB NOT NULL,
        created_at TIMESTAMPTZ NOT NULL,
        expires_at TIMESTAMPTZ NOT NULL,
        revoked_at TIMESTAMPTZ NULL
      )`);
      await c.query(`CREATE INDEX IF NOT EXISTS nexo_mobile_sessions_expires_idx ON nexo_mobile_sessions(expires_at)`);
      await c.query(`CREATE TABLE IF NOT EXISTS nexo_mobile_tickets (
        protocol VARCHAR(128) PRIMARY KEY,
        device_id VARCHAR(128) NOT NULL,
        hub_node_id VARCHAR(128) NOT NULL,
        subject VARCHAR(120) NOT NULL,
        description TEXT NOT NULL,
        priority VARCHAR(2) NOT NULL,
        status VARCHAR(24) NOT NULL,
        correlation_id VARCHAR(128) NOT NULL,
        created_at TIMESTAMPTZ NOT NULL,
        updated_at TIMESTAMPTZ NOT NULL
      )`);
      await c.query(`CREATE INDEX IF NOT EXISTS nexo_mobile_tickets_device_created_idx ON nexo_mobile_tickets(device_id,created_at DESC)`);
      await c.query('SELECT 1');
    }finally{c.release();}
    return{ok:true,persistence:this.persistence()};
  }
  async close(){if(this.pool)await this.pool.end();this.pool=null;}
  ensure(){if(!this.pool)throw err('MOBILE_STORE_NOT_READY',503);}
  async cleanup(){this.ensure();await this.pool.query('DELETE FROM nexo_mobile_pairings WHERE expires_at < NOW() AND used_at IS NULL');await this.pool.query('DELETE FROM nexo_mobile_sessions WHERE expires_at < NOW() OR revoked_at IS NOT NULL');}
  async putPairing(rec){this.ensure();await this.cleanup();await this.pool.query(`INSERT INTO nexo_mobile_pairings(pairing_id,token_hash,hub_node_id,issuer_node_id,scopes,created_at,expires_at,used_at) VALUES($1,$2,$3,$4,$5::jsonb,$6,$7,$8) ON CONFLICT (pairing_id) DO NOTHING`,[rec.pairingId,rec.tokenHash,rec.hubNodeId,rec.issuerNodeId,JSON.stringify(rec.scopes),rec.createdAt,rec.expiresAt,rec.usedAt||null]);return clone(rec);}
  async consumePairing(pairingId,tokenHash){
    this.ensure();const c=await this.pool.connect();
    try{await c.query('BEGIN');const r=await c.query(`SELECT pairing_id,token_hash,hub_node_id,issuer_node_id,scopes,created_at,expires_at,used_at FROM nexo_mobile_pairings WHERE pairing_id=$1 FOR UPDATE`,[pairingId]);if(!r.rowCount){await c.query('ROLLBACK');throw err('MOBILE_PAIRING_NOT_FOUND',404);}const x=r.rows[0];if(x.used_at){await c.query('ROLLBACK');throw err('MOBILE_PAIRING_USED',409);}if(new Date(x.expires_at).getTime()<=Date.now()){await c.query('ROLLBACK');throw err('MOBILE_PAIRING_EXPIRED',410);}if(String(x.token_hash)!==String(tokenHash)){await c.query('ROLLBACK');throw err('MOBILE_PAIRING_TOKEN_INVALID',401);}const u=await c.query('UPDATE nexo_mobile_pairings SET used_at=NOW() WHERE pairing_id=$1 AND used_at IS NULL RETURNING used_at',[pairingId]);if(!u.rowCount){await c.query('ROLLBACK');throw err('MOBILE_PAIRING_USED',409);}await c.query('COMMIT');return{pairingId:x.pairing_id,tokenHash:x.token_hash,hubNodeId:x.hub_node_id,issuerNodeId:x.issuer_node_id,scopes:Array.isArray(x.scopes)?x.scopes:[],createdAt:new Date(x.created_at).toISOString(),expiresAt:new Date(x.expires_at).toISOString(),usedAt:new Date(u.rows[0].used_at).toISOString()};}catch(e){if(!/^MOBILE_/.test(String(e.message||'')))try{await c.query('ROLLBACK');}catch{}throw e;}finally{c.release();}
  }
  async putSession(rec){this.ensure();await this.cleanup();await this.pool.query(`INSERT INTO nexo_mobile_sessions(token_hash,device_id,label,hub_node_id,scopes,created_at,expires_at,revoked_at) VALUES($1,$2,$3,$4,$5::jsonb,$6,$7,$8) ON CONFLICT (token_hash) DO UPDATE SET device_id=EXCLUDED.device_id,label=EXCLUDED.label,hub_node_id=EXCLUDED.hub_node_id,scopes=EXCLUDED.scopes,created_at=EXCLUDED.created_at,expires_at=EXCLUDED.expires_at,revoked_at=EXCLUDED.revoked_at`,[rec.tokenHash,rec.deviceId,rec.label,rec.hubNodeId,JSON.stringify(rec.scopes),rec.createdAt,rec.expiresAt,rec.revokedAt||null]);return clone(rec);}
  async getSession(tokenHash){this.ensure();await this.cleanup();const r=await this.pool.query(`SELECT token_hash,device_id,label,hub_node_id,scopes,created_at,expires_at,revoked_at FROM nexo_mobile_sessions WHERE token_hash=$1 AND revoked_at IS NULL AND expires_at>NOW()`,[tokenHash]);if(!r.rowCount)return null;const x=r.rows[0];return{tokenHash:x.token_hash,deviceId:x.device_id,label:x.label,hubNodeId:x.hub_node_id,scopes:Array.isArray(x.scopes)?x.scopes:[],createdAt:new Date(x.created_at).toISOString(),expiresAt:new Date(x.expires_at).toISOString(),revokedAt:x.revoked_at?new Date(x.revoked_at).toISOString():null};}
  async putTicket(rec){this.ensure();await this.pool.query(`INSERT INTO nexo_mobile_tickets(protocol,device_id,hub_node_id,subject,description,priority,status,correlation_id,created_at,updated_at) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10) ON CONFLICT (protocol) DO NOTHING`,[rec.protocol,rec.deviceId,rec.hubNodeId,rec.subject,rec.description,rec.priority,rec.status,rec.correlationId,rec.createdAt,rec.updatedAt]);return clone(rec);}
  async listTickets(deviceId){this.ensure();const r=await this.pool.query(`SELECT protocol,device_id,hub_node_id,subject,description,priority,status,correlation_id,created_at,updated_at FROM nexo_mobile_tickets WHERE device_id=$1 ORDER BY created_at DESC LIMIT 100`,[deviceId]);return r.rows.map(x=>({protocol:x.protocol,deviceId:x.device_id,hubNodeId:x.hub_node_id,subject:x.subject,description:x.description,priority:x.priority,status:x.status,correlationId:x.correlation_id,createdAt:new Date(x.created_at).toISOString(),updatedAt:new Date(x.updated_at).toISOString()}));}
}

export function createMobileStore({databaseUrl='',requirePersistent=false,now=()=>Date.now(),PoolClass=null}={}){
  if(String(databaseUrl||'').trim())return new PostgresMobileStore({databaseUrl,PoolClass});
  if(requirePersistent)throw err('MOBILE_DATABASE_URL_REQUIRED',503);
  return new MemoryMobileStore({now});
}
