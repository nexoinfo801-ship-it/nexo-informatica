const qs=(s)=>document.querySelector(s), qsa=(s)=>[...document.querySelectorAll(s)];
const toast=qs('#toast');
function showToast(text){toast.textContent=text;toast.classList.add('show');setTimeout(()=>toast.classList.remove('show'),2200)}
function networkRender(){
  const online=navigator.onLine;
  qs('#onlineText').textContent=online?'ONLINE':'OFFLINE';
  qs('#onlinePill').classList.toggle('offline',!online);
  qs('#deviceConnection').textContent=online?'Online':'Offline';
  qs('#syncStatus').textContent=online?'● Normal':'● Fila local';
}
window.addEventListener('online',()=>{networkRender();showToast('Conexão restaurada. Nenhum conteúdo foi enviado automaticamente.')});
window.addEventListener('offline',()=>{networkRender();showToast('Modo offline ativo. O NEXO continuará usando a fila local.')});
networkRender();

const DEFAULT_GATEWAY = location.protocol.startsWith('http')
  ? location.origin
  : 'https://gateway.nexo.sideproject.cyou';

async function checkGateway(){
  const start=performance.now();
  try{
    const r=await fetch(DEFAULT_GATEWAY+'/health',{cache:'no-store',signal:AbortSignal.timeout(4500)});
    if(!r.ok) throw new Error('health');
    const data=await r.json();
    const ms=Math.round(performance.now()-start);
    qs('#gatewayStatus').textContent='● Online';
    qs('#latencyText').textContent='Latência: '+ms+' ms';
    qs('#deviceConnection').textContent='Online';
    return data;
  }catch{
    qs('#gatewayStatus').textContent='● Gateway indisponível';
    qs('#latencyText').textContent='Sem resposta do Gateway';
    return null;
  }
}

async function loadBootstrap(){
  try{
    const r=await fetch(DEFAULT_GATEWAY+'/v1/bootstrap',{cache:'no-store',signal:AbortSignal.timeout(4500)});
    if(!r.ok) return null;
    const data=await r.json();
    if(!data?.ok || data?.envelope?.algorithm!=='ES256') return null;
    sessionStorage.setItem('nexo.bootstrap',JSON.stringify(data.envelope));
    return data.envelope;
  }catch{return null}
}

Promise.all([checkGateway(),loadBootstrap()]).then(([health,bootstrap])=>{
  if(health && bootstrap) showToast('Gateway e bootstrap seguro carregados.');
});
qs('#refreshStatus').addEventListener('click',async()=>{
  await Promise.all([checkGateway(),loadBootstrap()]);
  showToast('Status atualizado.');
});

const views={
  chamados:['Chamados','Fila de chamados integrada ao TICKET_ID do NEXO.'],
  conversa:['Conversa com a NEXA','A conversa real será ligada ao Remote Relay mantendo histórico e consentimento de envio.'],
  aprovacoes:['Aprovações','Challenges assinados ECDSA P-256 serão exibidos aqui.'],
  relatorios:['Relatórios','Relatórios técnicos e evidências de homologação serão exibidos aqui.'],
  perfil:['Perfil MASTER','Dispositivo, enrollment, chaves públicas e estado de autorização.']
};
qsa('.tab').forEach(btn=>btn.addEventListener('click',()=>{
  qsa('.tab').forEach(x=>x.classList.remove('active'));btn.classList.add('active');
  const v=btn.dataset.view;
  if(v==='inicio'){qs('#viewInicio').hidden=false;qs('#secondaryView').hidden=true;return}
  qs('#viewInicio').hidden=true;qs('#secondaryView').hidden=false;
  qs('#secondaryTitle').textContent=views[v][0];qs('#secondaryText').textContent=views[v][1];
}));
qs('#backBtn').addEventListener('click',()=>qsa('.tab')[0].click());

qsa('.quick-grid button').forEach(b=>b.addEventListener('click',()=>showToast({
  diagnostico:'Diagnóstico sanitizado preparado para integração.',
  conversa:'Abrindo conversa NEXA...',
  'novo-chamado':'Novo chamado será vinculado a um TICKET_ID.',
  qr:'Leitor QR será habilitado no fluxo de enrollment.'
}[b.dataset.action]||'Ação preparada.')));

function b64u(bytes){let s='';for(const b of new Uint8Array(bytes))s+=String.fromCharCode(b);return btoa(s).replace(/\+/g,'-').replace(/\//g,'_').replace(/=+$/,'')}
function stableObj(v){if(Array.isArray(v))return v.map(stableObj);if(v&&typeof v==='object')return Object.fromEntries(Object.keys(v).sort().map(k=>[k,stableObj(v[k])]));return v}
function canonical(v){return JSON.stringify(stableObj(v))}
function openDb(){return new Promise((resolve,reject)=>{const r=indexedDB.open('nexo-ios-r11',1);r.onupgradeneeded=()=>{if(!r.result.objectStoreNames.contains('keys'))r.result.createObjectStore('keys')};r.onsuccess=()=>resolve(r.result);r.onerror=()=>reject(r.error)})}
async function dbSet(k,v){const db=await openDb();return new Promise((resolve,reject)=>{const tx=db.transaction('keys','readwrite');tx.objectStore('keys').put(v,k);tx.oncomplete=()=>resolve();tx.onerror=()=>reject(tx.error)})}
async function dbGet(k){const db=await openDb();return new Promise((resolve,reject)=>{const r=db.transaction('keys').objectStore('keys').get(k);r.onsuccess=()=>resolve(r.result);r.onerror=()=>reject(r.error)})}
function runIosCheck(){const ua=navigator.userAgent;qs('#iosCheck').textContent=/iPhone|iPad|iPod/.test(ua)?'● iOS detectado':'● Navegador não-iOS';qs('#cryptoCheck').textContent=globalThis.crypto?.subtle?'● Disponível':'● Indisponível';const standalone=window.matchMedia('(display-mode: standalone)').matches||navigator.standalone===true;qs('#pwaCheck').textContent=standalone?'● Standalone':'● Safari / não instalado'}
qs('#runIosCheck')?.addEventListener('click',()=>{runIosCheck();showToast('Diagnóstico iPhone atualizado.')});runIosCheck();
async function ensureDeviceKeyPair(){let record=await dbGet('deviceKeyPair');if(record?.privateKey&&record?.publicKey)return record;const kp=await crypto.subtle.generateKey({name:'ECDSA',namedCurve:'P-256'},false,['sign','verify']);record={privateKey:kp.privateKey,publicKey:kp.publicKey};await dbSet('deviceKeyPair',record);return record}
async function postJson(path,body={}){const r=await fetch(DEFAULT_GATEWAY+path,{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify(body)});const d=await r.json().catch(()=>({}));if(!r.ok)throw new Error(d.error||('HTTP_'+r.status));return d}
async function enrollIphone(){try{qs('#enrollCheck').textContent='Gerando chave...';const kp=await ensureDeviceKeyPair();const publicJwk=await crypto.subtle.exportKey('jwk',kp.publicKey);const c=(await postJson('/v1/mobile/lab/enroll/challenge')).challenge;const payload={schema:'NEXO_MOBILE_ENROLLMENT_V1',challengeId:c.challengeId,deviceId:c.deviceId,nonce:c.nonce,publicJwk};const sig=await crypto.subtle.sign({name:'ECDSA',hash:'SHA-256'},kp.privateKey,new TextEncoder().encode(canonical(payload)));await postJson('/v1/mobile/lab/enroll/complete',{...payload,signature:b64u(sig)});localStorage.setItem('nexo.deviceId',c.deviceId);qs('#enrollCheck').textContent='● Ativo';showToast('iPhone enrollado no LAB com ECDSA P-256.')}catch(e){qs('#enrollCheck').textContent='● '+e.message;showToast('Enrollment não concluído: '+e.message)}}
qs('#enrollBtn')?.addEventListener('click',enrollIphone);
async function fieldApprove(){try{const deviceId=localStorage.getItem('nexo.deviceId');if(!deviceId)throw new Error('IPHONE_NOT_ENROLLED');const kp=await ensureDeviceKeyPair();const c=(await postJson('/v1/mobile/lab/challenge',{deviceId,action:'APPROVE'})).challenge;const payload={schema:c.schema,ticketId:c.ticketId,challengeId:c.challengeId,correlationId:c.correlationId,deviceId:c.deviceId,audience:c.audience,action:c.action,risk:c.risk,nonce:c.nonce,issuedAt:c.issuedAt,expiresAt:c.expiresAt,payloadHash:c.payloadHash};const sig=await crypto.subtle.sign({name:'ECDSA',hash:'SHA-256'},kp.privateKey,new TextEncoder().encode(canonical(payload)));const out=await postJson('/v1/mobile/lab/decision',{...payload,signature:b64u(sig)});showToast('APPROVE assinado e aceito: '+out.ack.challengeId.slice(0,8)+'…')}catch(e){showToast('Teste APPROVE falhou: '+e.message)}}
qs('#fieldApproveBtn')?.addEventListener('click',fieldApprove);
qs('#approveBtn').addEventListener('click',fieldApprove);
qs('#denyBtn').addEventListener('click',()=>showToast('DENY será ligado ao próximo challenge do MASTER.'));

if('serviceWorker' in navigator) navigator.serviceWorker.register('./sw.js').catch(()=>{});
