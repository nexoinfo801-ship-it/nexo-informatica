import test from 'node:test';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import { buildCentral } from '../src/core.mjs';
import { MemoryStore } from '../src/store-memory.mjs';

const licenseKey='NEXO.TEST.SIGNED.LICENSE.KEY.001';
const installId='inst-12345678';
function setup(extra={}) {
  const store=new MemoryStore();
  const auth=store.seedLicense({licenseKey,installId,serial:'NX-SERIAL-001'});
  const central=buildCentral({store,config:{publicGatewayUrl:'https://gateway.nexo.sideproject.cyou',...extra}});
  return {store,auth,central};
}
const body=(action,more={})=>({action,product:'NEXO_ERP_PRO',version:'22.8.3',install_id:installId,license_key:licenseKey,...more});

test('health works without license', async()=>{ const {central}=setup(); const r=await central({action:'health'},crypto.randomUUID()); assert.equal(r.status,200); assert.equal(r.body.ok,true); });
test('invalid license fails closed', async()=>{ const {central}=setup(); await assert.rejects(()=>central({...body('support_status'),license_key:'XXXXXXXXXXXXXXXX'},crypto.randomUUID()),/AUTH_INVALID_LICENSE_OR_INSTALLATION/); });
test('support create/status/message/close roundtrip', async()=>{
  const {central}=setup();
  const c=await central(body('support_create',{ticket:{local_protocol:'SUP-LOCAL-1',category:'Sistema',priority:'Normal',subject:'Teste remoto',description:'Mensagem inicial'},diagnostic:{install_id:installId}}),crypto.randomUUID());
  assert.equal(c.body.ok,true); const id=c.body.ticket.id;
  const s=await central(body('support_status'),crypto.randomUUID()); assert.equal(s.body.tickets.length,1);
  const m=await central(body('support_message',{ticket_id:id,message:'Nova mensagem'}),crypto.randomUUID()); assert.equal(m.body.ticket.messages.length,1);
  const x=await central(body('support_client_close',{ticket_id:id}),crypto.randomUUID()); assert.equal(x.body.ticket.status,'Fechado');
});
test('request id is idempotent for ticket creation', async()=>{
  const {central}=setup(); const rid=crypto.randomUUID();
  const b=body('support_create',{ticket:{category:'Sistema',priority:'Normal',subject:'Duplicado',description:'Idempotente'}});
  const a=await central(b,rid), c=await central(b,rid); assert.equal(a.body.ticket.id,c.body.ticket.id);
});
test('request id cannot be reused for a different action', async()=>{
  const {central}=setup(); const rid=crypto.randomUUID();
  await central(body('support_status'),rid);
  await assert.rejects(()=>central(body('license_ack',{state:'active'}),rid),/REQUEST_ID_REUSED/);
});
test('cached request id never bypasses license authentication', async()=>{
  const {central}=setup(); const rid=crypto.randomUUID();
  await central(body('support_status'),rid);
  await assert.rejects(()=>central({...body('support_status'),license_key:'INVALID.INVALID.INVALID'},rid),/AUTH_INVALID_LICENSE_OR_INSTALLATION/);
});
test('cached request id is scoped to the authenticated installation', async()=>{
  const {central,store}=setup(); const rid=crypto.randomUUID();
  await central(body('support_status'),rid);
  store.seedLicense({licenseKey:'NEXO.TEST.SIGNED.LICENSE.KEY.002',installId:'inst-99999999',serial:'NX-SERIAL-002'});
  const other=buildCentral({store});
  await assert.rejects(()=>other({action:'support_status',product:'NEXO_ERP_PRO',install_id:'inst-99999999',license_key:'NEXO.TEST.SIGNED.LICENSE.KEY.002'},rid),/REQUEST_ID_REUSED/);
});
test('diagnostic secrets and personal document keys are redacted server-side', async()=>{
  const {central,store}=setup();
  await central(body('support_create',{ticket:{category:'Sistema',priority:'Normal',subject:'Sanitização',description:'Teste'},diagnostic:{token:'SECRET-TOKEN',cpf:'52998224725',nested:{senha:'Senha#123',ok:'safe'}}}),crypto.randomUUID());
  const stored=[...store.tickets.values()][0].diagnostic;
  assert.equal(stored.token,'[REDACTED]'); assert.equal(stored.cpf,'[REDACTED]'); assert.equal(stored.nested.senha,'[REDACTED]'); assert.equal(stored.nested.ok,'safe');
});
test('ticket isolation blocks different installation', async()=>{
  const {store,central}=setup();
  const c=await central(body('support_create',{ticket:{category:'Sistema',priority:'Normal',subject:'Isolado',description:'Teste'}}),crypto.randomUUID());
  store.seedLicense({licenseKey:'NEXO.TEST.SIGNED.LICENSE.KEY.002',installId:'inst-99999999',serial:'NX-SERIAL-002'});
  const other=buildCentral({store});
  await assert.rejects(()=>other({action:'support_message',product:'NEXO_ERP_PRO',install_id:'inst-99999999',license_key:'NEXO.TEST.SIGNED.LICENSE.KEY.002',ticket_id:c.body.ticket.id,message:'intrusão'},crypto.randomUUID()),/TICKET_NOT_FOUND/);
});
test('license status fails until delegated signer is configured', async()=>{ const {central}=setup(); await assert.rejects(()=>central(body('license_status',{serial:'NX-SERIAL-001'}),crypto.randomUUID()),/LICENSE_STATUS_SIGNER_NOT_CONFIGURED/); });
test('license status signs ES256 P-256 compact envelope when configured', async()=>{
  const {privateKey,publicKey}=crypto.generateKeyPairSync('ec',{namedCurve:'prime256v1'});
  const pem=privateKey.export({format:'pem',type:'pkcs8'}).toString();
  const {central}=setup({licenseStatusKid:'lic-status-test1',licenseStatusPrivatePemB64:Buffer.from(pem).toString('base64')});
  const r=await central(body('license_status',{serial:'NX-SERIAL-001'}),crypto.randomUUID());
  const [p64,s64]=r.body.status_envelope.split('.');
  const ok=crypto.verify('sha256',Buffer.from(p64,'utf8'),{key:publicKey,dsaEncoding:'ieee-p1363'},Buffer.from(s64,'base64url'));
  assert.equal(ok,true); const p=JSON.parse(Buffer.from(p64,'base64url').toString('utf8')); assert.equal(p.type,'NEXO_LICENSE_STATUS'); assert.equal(p.install_id,installId);
});
