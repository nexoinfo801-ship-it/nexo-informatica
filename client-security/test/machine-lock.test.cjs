'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const M=require('../machine-lock.cjs');

const machineGuid='11111111-2222-3333-4444-555555555555';
const installId='NXM-AAAA-BBBB-CCCC-DDDD';
const compact='NEXO.TEST.LICENSE.SIGNED';
const expiresAt='2026-10-11T23:59:59.000Z';
const binding=M.machineBinding({machineGuid,installId});
const payload={
  install_id:installId,
  machine_binding:binding,
  beta_policy:{channel:'BETA',expires_at:expiresAt,max_devices:1,transfer_allowed:false}
};

test('same machine is authorized',()=>{
  const r=M.evaluateMachineLock({licensePayload:payload,machineGuid,installId,now:new Date('2026-09-11T12:00:00Z')});
  assert.equal(r.ok,true); assert.equal(r.code,'AUTHORIZED');
});

test('copied folder on another machine is blocked',()=>{
  const r=M.evaluateMachineLock({licensePayload:payload,machineGuid:'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',installId,now:new Date('2026-09-11T12:00:00Z')});
  assert.equal(r.ok,false); assert.equal(r.code,'MACHINE_BINDING_MISMATCH');
});

test('different install id is blocked before machine comparison',()=>{
  const r=M.evaluateMachineLock({licensePayload:payload,machineGuid,installId:'NXM-OTHER',now:new Date('2026-09-11T12:00:00Z')});
  assert.equal(r.ok,false); assert.equal(r.code,'LICENSE_INSTALL_ID_MISMATCH');
});

test('expired beta is blocked',()=>{
  const r=M.evaluateMachineLock({licensePayload:payload,machineGuid,installId,now:new Date('2026-11-01T12:00:00Z')});
  assert.equal(r.ok,false); assert.equal(r.code,'BETA_EXPIRED');
});

test('beta requires one device and no transfer',()=>{
  assert.equal(M.validateBetaPolicy({channel:'BETA',expires_at:expiresAt,max_devices:2,transfer_allowed:false}).code,'BETA_SINGLE_DEVICE_REQUIRED');
  assert.equal(M.validateBetaPolicy({channel:'BETA',expires_at:expiresAt,max_devices:1,transfer_allowed:true}).code,'BETA_TRANSFER_MUST_BE_DISABLED');
});

test('binding record stores fingerprint, not the license key',()=>{
  const r=M.buildBetaBindingRecord({machineGuid,installId,licenseCompact:compact,expiresAt});
  assert.equal(r.license_fingerprint.length,64);
  assert.equal(JSON.stringify(r).includes(compact),false);
});
