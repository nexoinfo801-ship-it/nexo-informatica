import crypto from 'node:crypto';

export function sha256(value){return crypto.createHash('sha256').update(String(value||''),'utf8').digest('hex');}

export class MemoryMobileStore{
  constructor({now=()=>Date.now()}={}){this.now=now;this.pairings=new Map();this.sessions=new Map();this.tickets=[];this.presence=new Map();}
  async init(){return {ok:true,persistence:'MEMORY'};}
  async close(){}
  async createPairing(record){this.pairings.set(record.pairingId,{...record});return {...record};}
  async consumePairing({pairingId,tokenHash}){const p=this.pairings.get(pairingId);if(!p)throw new Error('MOBILE_PAIRING_NOT_FOUND');if(p.consumedAt)throw new Error('MOBILE_PAIRING_REPLAY');if(this.now()>Date.parse(p.expiresAt))throw new Error('MOBILE_PAIRING_EXPIRED');if(p.tokenHash!==tokenHash)throw new Error('MOBILE_PAIRING_TOKEN_INVALID');p.consumedAt=new Date(this.now()).toISOString();return {...p};}
  async createSession(record){this.sessions.set(record.sessionHash,{...record});return {...record};}
  async getSession(sessionHash){const s=this.sessions.get(sessionHash);if(!s)return null;if(s.revokedAt)return null;if(this.now()>Date.parse(s.expiresAt))return {...s,expired:true};s.lastSeenAt=new Date(this.now()).toISOString();return {...s};}
  async addTicket(record){this.tickets.push({...record});return {...record};}
  async listTickets(sessionId){return this.tickets.filter(x=>x.sessionId===sessionId).map(x=>({...x})).sort((a,b)=>String(b.createdAt).localeCompare(String(a.createdAt)));}
  async heartbeat({nexaNodeId,at}){this.presence.set(nexaNodeId,at);return {nexaNodeId,lastSeenAt:at};}
  async presenceOf(nexaNodeId){return this.presence.get(nexaNodeId)||null;}
}