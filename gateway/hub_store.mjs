const MAX_QUEUE_PER_ROLE = 500;

function nowIso(){ return new Date().toISOString(); }
function safeInt(value,fallback=0){ const n=Number(value); return Number.isFinite(n)?Math.trunc(n):fallback; }

export class MemoryHubStore {
  constructor(){ this.sequence=0; this.items=[]; this.byId=new Map(); }
  persistence(){ return 'MEMORY_LAB'; }
  async init(){ return {ok:true,persistence:this.persistence()}; }
  async close(){}
  async cleanup(){
    const now=Date.now();
    this.items=this.items.filter(x=>Date.parse(x.expiresAt)>=now);
    this.byId=new Map(this.items.map(x=>[x.messageId,x]));
  }
  async put({messageId,envelopeHash,targetRole,targetNodeId='',expiresAt,envelope}){
    await this.cleanup();
    const prior=this.byId.get(messageId);
    if(prior){
      return {inserted:false,duplicate:true,conflict:prior.envelopeHash!==envelopeHash,sequence:prior.sequence};
    }
    const item={sequence:++this.sequence,messageId,envelopeHash,targetRole,targetNodeId:targetNodeId||'',expiresAt,receivedAt:nowIso(),envelope};
    this.items.push(item); this.byId.set(messageId,item);
    const roleItems=this.items.filter(x=>x.targetRole===targetRole).sort((a,b)=>b.sequence-a.sequence);
    if(roleItems.length>MAX_QUEUE_PER_ROLE){
      const keep=new Set(roleItems.slice(0,MAX_QUEUE_PER_ROLE).map(x=>x.sequence));
      this.items=this.items.filter(x=>x.targetRole!==targetRole||keep.has(x.sequence));
      this.byId=new Map(this.items.map(x=>[x.messageId,x]));
    }
    return {inserted:true,duplicate:false,conflict:false,sequence:item.sequence};
  }
  async poll({role,nodeId,afterSequence=0,limit=20}){
    await this.cleanup();
    return this.items
      .filter(x=>x.targetRole===role&&x.sequence>safeInt(afterSequence)&&(!x.targetNodeId||x.targetNodeId===nodeId))
      .sort((a,b)=>a.sequence-b.sequence)
      .slice(0,Math.max(1,Math.min(50,safeInt(limit,20))))
      .map(x=>({sequence:x.sequence,receivedAt:x.receivedAt,envelope:x.envelope}));
  }
  async ack({role,nodeId,messageIds=[]}){
    await this.cleanup();
    const ids=new Set(messageIds.map(String));
    let acked=0;
    this.items=this.items.filter(x=>{
      if(x.targetRole!==role||!ids.has(x.messageId)|| (x.targetNodeId&&x.targetNodeId!==nodeId)) return true;
      this.byId.delete(x.messageId); acked++; return false;
    });
    return {acked};
  }
  async status(){
    await this.cleanup();
    const queues={CLIENTE:0,MASTER:0,NEXA:0};
    for(const item of this.items) queues[item.targetRole]=(queues[item.targetRole]||0)+1;
    return {persistence:this.persistence(),queues,total:this.items.length};
  }
}

export class PostgresHubStore {
  constructor({databaseUrl}){ this.databaseUrl=String(databaseUrl||'').trim(); this.pool=null; }
  persistence(){ return 'POSTGRES'; }
  async init(){
    if(!this.databaseUrl) throw new Error('HUB_DATABASE_URL_REQUIRED');
    const {Pool}=await import('pg');
    const sslRequired=/sslmode=require/i.test(this.databaseUrl) || (!/\.railway\.internal(?::|\/|$)/i.test(this.databaseUrl) && !/localhost|127\.0\.0\.1/i.test(this.databaseUrl));
    this.pool=new Pool({connectionString:this.databaseUrl,max:5,idleTimeoutMillis:30000,connectionTimeoutMillis:8000,ssl:sslRequired?{rejectUnauthorized:false}:undefined});
    const client=await this.pool.connect();
    try{
      await client.query(`CREATE TABLE IF NOT EXISTS nexo_hub_messages (
        sequence BIGSERIAL PRIMARY KEY,
        message_id VARCHAR(128) UNIQUE NOT NULL,
        envelope_hash CHAR(64) NOT NULL,
        target_role VARCHAR(16) NOT NULL CHECK (target_role IN ('CLIENTE','MASTER','NEXA')),
        target_node_id VARCHAR(96) NOT NULL DEFAULT '',
        expires_at TIMESTAMPTZ NOT NULL,
        received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
        envelope JSONB NOT NULL
      )`);
      await client.query(`CREATE INDEX IF NOT EXISTS nexo_hub_messages_role_sequence_idx ON nexo_hub_messages(target_role,sequence)`);
      await client.query(`CREATE INDEX IF NOT EXISTS nexo_hub_messages_expires_idx ON nexo_hub_messages(expires_at)`);
      await client.query('SELECT 1');
    } finally { client.release(); }
    return {ok:true,persistence:this.persistence()};
  }
  async close(){ if(this.pool) await this.pool.end(); }
  async cleanup(){ if(!this.pool) throw new Error('HUB_STORE_NOT_READY'); await this.pool.query('DELETE FROM nexo_hub_messages WHERE expires_at < NOW()'); }
  async put({messageId,envelopeHash,targetRole,targetNodeId='',expiresAt,envelope}){
    await this.cleanup();
    const client=await this.pool.connect();
    try{
      await client.query('BEGIN');
      const inserted=await client.query(`INSERT INTO nexo_hub_messages(message_id,envelope_hash,target_role,target_node_id,expires_at,envelope)
        VALUES($1,$2,$3,$4,$5,$6::jsonb) ON CONFLICT (message_id) DO NOTHING RETURNING sequence`,
        [messageId,envelopeHash,targetRole,targetNodeId||'',expiresAt,JSON.stringify(envelope)]);
      if(inserted.rowCount===0){
        const prior=await client.query('SELECT sequence,envelope_hash FROM nexo_hub_messages WHERE message_id=$1',[messageId]);
        await client.query('COMMIT');
        if(!prior.rowCount) throw new Error('HUB_DEDUPE_LOOKUP_FAILED');
        return {inserted:false,duplicate:true,conflict:prior.rows[0].envelope_hash!==envelopeHash,sequence:Number(prior.rows[0].sequence)};
      }
      await client.query(`DELETE FROM nexo_hub_messages WHERE sequence IN (
        SELECT sequence FROM nexo_hub_messages WHERE target_role=$1 ORDER BY sequence DESC OFFSET $2
      )`,[targetRole,MAX_QUEUE_PER_ROLE]);
      await client.query('COMMIT');
      return {inserted:true,duplicate:false,conflict:false,sequence:Number(inserted.rows[0].sequence)};
    }catch(error){ try{await client.query('ROLLBACK');}catch{} throw error; }
    finally{ client.release(); }
  }
  async poll({role,nodeId,afterSequence=0,limit=20}){
    await this.cleanup();
    const result=await this.pool.query(`SELECT sequence,received_at,envelope FROM nexo_hub_messages
      WHERE target_role=$1 AND sequence>$2 AND expires_at>=NOW() AND (target_node_id='' OR target_node_id=$3)
      ORDER BY sequence ASC LIMIT $4`,[role,safeInt(afterSequence),nodeId,Math.max(1,Math.min(50,safeInt(limit,20)))]);
    return result.rows.map(r=>({sequence:Number(r.sequence),receivedAt:new Date(r.received_at).toISOString(),envelope:r.envelope}));
  }
  async ack({role,nodeId,messageIds=[]}){
    await this.cleanup();
    const ids=[...new Set(messageIds.map(String))].slice(0,100);
    if(!ids.length) return {acked:0};
    const result=await this.pool.query(`DELETE FROM nexo_hub_messages
      WHERE target_role=$1 AND message_id = ANY($2::text[]) AND (target_node_id='' OR target_node_id=$3)
      RETURNING message_id`,[role,ids,nodeId]);
    return {acked:result.rowCount};
  }
  async status(){
    await this.cleanup();
    const result=await this.pool.query(`SELECT target_role,COUNT(*)::int AS count FROM nexo_hub_messages WHERE expires_at>=NOW() GROUP BY target_role`);
    const queues={CLIENTE:0,MASTER:0,NEXA:0}; let total=0;
    for(const row of result.rows){ queues[row.target_role]=Number(row.count)||0; total+=Number(row.count)||0; }
    return {persistence:this.persistence(),queues,total};
  }
}

export function createHubStore({databaseUrl='',requirePersistent=false}={}){
  if(String(databaseUrl||'').trim()) return new PostgresHubStore({databaseUrl});
  if(requirePersistent) throw new Error('HUB_PERSISTENCE_REQUIRED');
  return new MemoryHubStore();
}
