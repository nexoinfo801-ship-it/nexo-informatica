import test from 'node:test';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import {createCommunicationHub} from '../hub.mjs';

const COMPAT='NEXO-SUITE-PRIME-R8-P5-MOBILE-20260911';
function stable(v){if(Array.isArray(v))return v.map(stable);if(v&&typeof v==='object')return Object.fromEntries(Object.keys(v).sort().map(k=>[k,stable(v[k])]));return v;}
function canonical(v){return JSON.stringify(stable(v));}
function sha(v){return crypto.createHash('sha256').update(canonical(v),'utf8').digest('hex');}
function sign(key,payload){return crypto.sign('sha256',Buffer.from(canonical(payload),'utf8'),{key,dsaEncoding:'ieee-p1363'}).toString('base64url');}
const gatewayKeys=crypto.generateKeyPairSync('ec',{namedCurve:'P-256'});
const hub=createCommunicationHub({privateKeyB64:Buffer.from(gatewayKeys.privateKey.export({type:'pkcs8',format:'pem'})).toString('base64'),publicJwk:gatewayKeys.publicKey.export({format:'jwk'}),compatibilityId:COMPAT});
function enroll(role,installId){const keys=crypto.generateKeyPairSync('ec',{namedCurve:'P-256'}),publicJwk=keys.publicKey.export({format:'jwk'}),ch=hub.challenge({role,installId}),payload={schema:'NEXO_HUB_ENROLLMENT_V1',compatibilityId:ch.compatibilityId,challengeId:ch.challengeId,nodeId:ch.nodeId,role:ch.role,installId:ch.installId,nonce:ch.nonce,publicJwk},out=hub.complete({...payload,signature:sign(keys.privateKey,payload)});return{role,nodeId:out.node.nodeId,credential:out.credential,key:keys.privateKey};}
function send(node,targetRole,type,payload,{targetNodeId='',correlationId=`COR-${crypto.randomUUID()}`}={}){const now=new Date(),env={schema:'NEXO_COMM_ENVELOPE_V1',schemaVersion:1,compatibilityId:COMPAT,messageId:`MSG-${crypto.randomUUID()}`,taskId:'',correlationId,sourceRole:node.role,sourceNodeId:node.nodeId,targetRole,targetNodeId, type,createdAt:now.toISOString(),expiresAt:new Date(now.getTime()+600000).toISOString(),payload,payloadHash:sha(payload)};return hub.send({credential:node.credential,envelope:env,signature:sign(node.key,env)});}
function poll(node){const p={schema:'NEXO_HUB_POLL_V1',nodeId:node.nodeId,role:node.role,issuedAt:new Date().toISOString(),nonce:crypto.randomBytes(12).toString('base64url'),limit:20,afterSequence:0};return hub.poll({credential:node.credential,poll:p,signature:sign(node.key,p)});}
function ack(node,ids){const a={schema:'NEXO_HUB_ACK_V1',nodeId:node.nodeId,role:node.role,issuedAt:new Date().toISOString(),nonce:crypto.randomBytes(12).toString('base64url'),messageIds:ids};return hub.ack({credential:node.credential,ack:a,signature:sign(node.key,a)});}

test('private CLIENTE reply is visible only to target node',()=>{const c1=enroll('CLIENTE','CLIENT-A'),c2=enroll('CLIENTE','CLIENT-B'),nexa=enroll('NEXA','NEXA-1');const sent=send(nexa,'CLIENTE','SUPPORT_MESSAGE',{answer:'ok'},{targetNodeId:c1.nodeId});assert.equal(sent.targetNodeId,c1.nodeId);assert.equal(poll(c2).messages.length,0);const p1=poll(c1);assert.equal(p1.messages.length,1);assert.equal(p1.messages[0].envelope.targetNodeId,c1.nodeId);assert.equal(ack(c2,[p1.messages[0].envelope.messageId]).acked,0);assert.equal(ack(c1,[p1.messages[0].envelope.messageId]).acked,1);});

test('target node role mismatch is rejected',()=>{const client=enroll('CLIENTE','CLIENT-C'),nexa=enroll('NEXA','NEXA-2');assert.throws(()=>send(nexa,'MASTER','SUPPORT_MESSAGE',{answer:'x'},{targetNodeId:client.nodeId}),/HUB_TARGET_NODE_INVALID/);});

test('protocol advertises node targeting and safe reconnect policy',()=>{const p=hub.protocol();assert.equal(p.protocolVersion,'1.1.0-lab');assert.equal(p.nodeTargeting,true);assert.equal(p.remoteMutation,false);assert.equal(p.autoSendOnReconnect,false);});
