'use strict';
const crypto=require('crypto');

const BINDING_DOMAIN='NEXO-ERP-PRO|MACHINE-BINDING|V1|';
const BETA_DOMAIN='NEXO-ERP-PRO|BETA-LICENSE|V1|';

function sha256Hex(v){return crypto.createHash('sha256').update(Buffer.from(String(v),'utf8')).digest('hex');}
function normalizeMachineGuid(v){return String(v||'').trim().toLowerCase();}
function normalizeInstallId(v){return String(v||'').trim();}

function machineBinding({machineGuid,installId}){
  const mg=normalizeMachineGuid(machineGuid),iid=normalizeInstallId(installId);
  if(!mg)throw new Error('MACHINE_GUID_REQUIRED');
  if(!iid)throw new Error('INSTALL_ID_REQUIRED');
  return sha256Hex(`${BINDING_DOMAIN}${mg}|${iid}`);
}

function licenseFingerprint(compact){
  const s=String(compact||'').trim();
  if(!s)throw new Error('LICENSE_REQUIRED');
  return sha256Hex(`${BETA_DOMAIN}${s}`);
}

function validateBetaPolicy(policy,{now=new Date()}={}){
  if(!policy||typeof policy!=='object')return {ok:false,code:'BETA_POLICY_MISSING'};
  if(policy.channel!=='BETA')return {ok:false,code:'BETA_CHANNEL_REQUIRED'};
  if(Number(policy.max_devices)!==1)return {ok:false,code:'BETA_SINGLE_DEVICE_REQUIRED'};
  if(policy.transfer_allowed!==false)return {ok:false,code:'BETA_TRANSFER_MUST_BE_DISABLED'};
  const exp=Date.parse(String(policy.expires_at||''));
  if(!Number.isFinite(exp))return {ok:false,code:'BETA_EXPIRY_INVALID'};
  if(now.getTime()>exp)return {ok:false,code:'BETA_EXPIRED'};
  return {ok:true};
}

function evaluateMachineLock({licensePayload,machineGuid,installId,storedBinding,now=new Date()}){
  if(!licensePayload||typeof licensePayload!=='object')return {ok:false,code:'LICENSE_PAYLOAD_MISSING'};
  const iid=normalizeInstallId(installId);
  if(String(licensePayload.install_id||'')!==iid)return {ok:false,code:'LICENSE_INSTALL_ID_MISMATCH'};
  const beta=validateBetaPolicy(licensePayload.beta_policy,{now});
  if(!beta.ok)return beta;
  let current;
  try{current=machineBinding({machineGuid,installId:iid});}catch(e){return {ok:false,code:e.message};}
  const expected=String(licensePayload.machine_binding||storedBinding||'').trim().toLowerCase();
  if(!expected)return {ok:false,code:'MACHINE_BINDING_MISSING'};
  if(!crypto.timingSafeEqual(Buffer.from(current,'hex'),Buffer.from(expected,'hex')))return {ok:false,code:'MACHINE_BINDING_MISMATCH'};
  return {ok:true,code:'AUTHORIZED',machine_binding:current};
}

function buildBetaBindingRecord({machineGuid,installId,licenseCompact,expiresAt}){
  const binding=machineBinding({machineGuid,installId});
  const exp=String(expiresAt||'').trim();
  if(!Number.isFinite(Date.parse(exp)))throw new Error('BETA_EXPIRY_INVALID');
  return Object.freeze({
    format:'NEXO_BETA_MACHINE_LOCK_V1',channel:'BETA',install_id:normalizeInstallId(installId),
    machine_binding:binding,license_fingerprint:licenseFingerprint(licenseCompact),expires_at:exp,
    max_devices:1,transfer_allowed:false,created_at:new Date().toISOString()
  });
}

module.exports={machineBinding,licenseFingerprint,validateBetaPolicy,evaluateMachineLock,buildBetaBindingRecord};
