const $=(s,r=document)=>r.querySelector(s), $$=(s,r=document)=>[...r.querySelectorAll(s)];
const enc=new TextEncoder();
const GATEWAY=location.protocol.startsWith('http')?location.origin:'https://gateway.nexo.sideproject.cyou';
const COMPAT='NEXO-SUITE-PRIME-R8-P5-MOBILE-20260911';
const DB_NAME='nexo-mobile-r12';
const STORE='kv';
let state={online:navigator.onLine,hub:null,session:null,messages:[],selected:null,lastSync:null,health:null};

function stable(v){if(Array.isArray(v))return v.map(stable);if(v&&typeof v==='object')return Object.fromEntries(Object.keys(v).sort().map(k=>[k,stable(v[k])]));return v}
function canonical(v){return JSON.stringify(stable(v))}
function b64u(buf){let s='';for(const b of new Uint8Array(buf))s+=String.fromCharCode(b);return btoa(s).replace(/\+/g,'-').replace(/\//g,'_').replace(/=+$/,'')}
async function sha256(v){return [...new Uint8Array(await crypto.subtle.digest('SHA-256',enc.encode(canonical(v))))].map(x=>x.toString(16).padStart(2,'0')).join('')}
function uuid(prefix=''){return prefix+crypto.randomUUID()}
function nowIso(){return new Date().toISOString()}
function esc(s){return String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]))}
function toast(t){const el=$('#toast');el.textContent=t;el.classList.add('show');clearTimeout(toast.t);toast.t=setTimeout(()=>el.classList.remove('show'),2600)}

function openDb(){return new Promise((res,rej)=>{const r=indexedDB.open(DB_NAME,1);r.onupgradeneeded=()=>{if(!r.result.objectStoreNames.contains(STORE))r.result.createObjectStore(STORE)};r.onsuccess=()=>res(r.result);r.onerror=()=>rej(r.error)})}
async function get(k){const db=await openDb();return new Promise((res,rej)=>{const r=db.transaction(STORE).objectStore(STORE).get(k);r.onsuccess=()=>res(r.result);r.onerror=()=>rej(r.error)})}
async function set(k,v){const db=await openDb();return new Promise((res,rej)=>{const tx=db.transaction(STORE,'readwrite');tx.objectStore(STORE).put(v,k);tx.oncomplete=()=>res();tx.onerror=()=>rej(tx.error)})}
async function del(k){const db=await openDb();return new Promise((res,rej)=>{const tx=db.transaction(STORE,'readwrite');tx.objectStore(STORE).delete(k);tx.oncomplete=()=>res();tx.onerror=()=>rej(tx.error)})}

async function api(path,opts={}){
 const ctl=new AbortController(),timer=setTimeout(()=>ctl.abort(),7000);
 try{const r=await fetch(GATEWAY+path,{cache:'no-store',...opts,signal:ctl.signal});const d=await r.json().catch(()=>({}));if(!r.ok)throw new Error(d.error||`HTTP_${r.status}`);return d}finally{clearTimeout(timer)}
}
async function post(path,body){return api(path,{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify(body)})}

async function ensureKeyPair(){let kp=await get('hub.keypair');if(kp?.privateKey&&kp?.publicKey)return kp;kp=await crypto.subtle.generateKey({name:'ECDSA',namedCurve:'P-256'},false,['sign','verify']);await set('hub.keypair',kp);return kp}
async function sign(privateKey,payload){return b64u(await crypto.subtle.sign({name:'ECDSA',hash:'SHA-256'},privateKey,enc.encode(canonical(payload))))}
async function installId(){let id=await get('hub.installId');if(!id){id='ios-master-'+crypto.randomUUID();await set('hub.installId',id)}return id}

async function loadSession(){state.session=await get('hub.session')||null;return state.session}
async function enrollHub(){
 if(!navigator.onLine)throw new Error('OFFLINE');
 const kp=await ensureKeyPair(), iid=await installId(), publicJwk=await crypto.subtle.exportKey('jwk',kp.publicKey);
 const ch=(await post('/v1/hub/enroll/challenge',{role:'MASTER',installId:iid})).challenge;
 const payload={schema:'NEXO_HUB_ENROLLMENT_V1',compatibilityId:ch.compatibilityId,challengeId:ch.challengeId,nodeId:ch.nodeId,role:ch.role,installId:ch.installId,nonce:ch.nonce,publicJwk};
 const out=await post('/v1/hub/enroll/complete',{...payload,signature:await sign(kp.privateKey,payload)});
 state.session={credential:out.credential,nodeId:out.node.nodeId,role:'MASTER',expiresAt:out.node.expiresAt,installId:iid};
 await set('hub.session',state.session); renderProfile();toast('iPhone ativado no Communication Hub.');return state.session;
}
async function resetEnrollment(){await del('hub.session');state.session=null;renderProfile();toast('Credencial local removida. A chave permanece protegida no aparelho.')}

async function hubRequest(path,payloadKey,payload){
 const session=state.session||await loadSession();if(!session)throw new Error('IPHONE_NOT_ENROLLED');
 const kp=await ensureKeyPair();
 return post(path,{credential:session.credential,[payloadKey]:payload,signature:await sign(kp.privateKey,payload)});
}
async function pollHub({quiet=false}={}){
 if(!navigator.onLine){if(!quiet)toast('Offline: mensagens locais preservadas.');return []}
 const session=state.session||await loadSession();if(!session){if(!quiet)toast('Ative este iPhone no Perfil primeiro.');return []}
 const poll={schema:'NEXO_HUB_POLL_V1',nodeId:session.nodeId,role:'MASTER',issuedAt:nowIso(),nonce:uuid('POLL-'),limit:50,afterSequence:0};
 try{
  const out=await hubRequest('/v1/hub/poll','poll',poll);
  state.messages=out.messages||[];state.lastSync=nowIso();await set('hub.inbox',state.messages);renderBadges();renderCurrent();if(!quiet)toast(`${state.messages.length} mensagem(ns) no Hub.`);return state.messages;
 }catch(e){if(String(e.message).includes('CREDENTIAL')){await del('hub.session');state.session=null;renderProfile()}if(!quiet)toast('Falha ao consultar Hub: '+e.message);return []}
}
async function ackMessages(ids){if(!ids?.length)return;const s=state.session;if(!s)return;const ack={schema:'NEXO_HUB_ACK_V1',nodeId:s.nodeId,role:'MASTER',issuedAt:nowIso(),nonce:uuid('ACK-'),messageIds:ids};await hubRequest('/v1/hub/ack','ack',ack)}

async function sendEnvelope({targetRole,targetNodeId='',type,payload,correlationId='',taskId=''},{queueOnFail=true}={}){
 const session=state.session||await loadSession();if(!session)throw new Error('IPHONE_NOT_ENROLLED');
 const created=new Date(),expires=new Date(created.getTime()+24*60*60*1000);
 const envelope={schema:'NEXO_COMM_ENVELOPE_V1',schemaVersion:1,compatibilityId:COMPAT,messageId:uuid('MSG-'),taskId,correlationId:correlationId||uuid('COR-'),sourceRole:'MASTER',sourceNodeId:session.nodeId,targetRole,targetNodeId:targetNodeId||undefined,type,createdAt:created.toISOString(),expiresAt:expires.toISOString(),payload,payloadHash:await sha256(payload)};
 if(!envelope.targetNodeId)delete envelope.targetNodeId;
 try{
   if(!navigator.onLine)throw new Error('OFFLINE');
   const kp=await ensureKeyPair();const out=await post('/v1/hub/send',{credential:session.credential,envelope,signature:await sign(kp.privateKey,envelope)});return out;
 }catch(e){
   if(!queueOnFail)throw e;
   const q=await get('hub.outbox')||[];q.push({envelope,queuedAt:nowIso()});await set('hub.outbox',q);renderOutboxCount();toast('Mensagem guardada localmente. Envio automático continua desativado.');return {queued:true};
 }
}
async function flushOutbox(){
 const q=await get('hub.outbox')||[];if(!q.length){toast('Nenhuma mensagem pendente.');return}
 if(!navigator.onLine){toast('Sem internet. A fila continua local.');return}
 const s=state.session||await loadSession();if(!s){toast('Ative este iPhone primeiro.');return}
 const kp=await ensureKeyPair(),remain=[];
 for(const item of q){try{await post('/v1/hub/send',{credential:s.credential,envelope:item.envelope,signature:await sign(kp.privateKey,item.envelope)})}catch{remain.push(item)}}
 await set('hub.outbox',remain);renderOutboxCount();toast(`${q.length-remain.length} enviada(s); ${remain.length} ainda pendente(s).`)
}

function msgEnv(item){return item?.envelope||{}}
function msgPriority(m){const p=String(m.payload?.priority||m.payload?.risk||'NORMAL').toUpperCase();return ['P0','P1','CRITICAL','ALTA','HIGH'].includes(p)?'high':['P2','MEDIA','MÉDIA','MEDIUM'].includes(p)?'medium':'low'}
function labelType(t){return({SUPPORT_INTAKE:'Triagem NEXA',SUPPORT_REQUEST:'Chamado',SUPPORT_MESSAGE:'Mensagem',SUPPORT_STATUS:'Status',DIAGNOSTIC_SUMMARY:'Diagnóstico',LICENSE_REQUEST:'Licença',LICENSE_STATUS:'Licença',PRIME_PROPOSAL:'Aprovação',PRIME_RESULT:'Decisão',SYSTEM_NOTICE:'Aviso'})[t]||t}
function ticketId(m){return m.payload?.ticketId||m.payload?.intakeId||m.correlationId||m.messageId}
function summary(m){return m.payload?.subject||m.payload?.title||m.payload?.summary?.message||m.payload?.message||labelType(m.type)}

async function health(){try{const start=performance.now(),h=await api('/health');state.health=h;$('#gatewayStatus').textContent='● Online';$('#latencyText').textContent=`Latência: ${Math.round(performance.now()-start)} ms`;$('#hubVersion').textContent=h.communication_hub_configured?'Hub ativo':'Hub indisponível';return h}catch{$('#gatewayStatus').textContent='● Indisponível';$('#latencyText').textContent='Sem resposta';return null}}
async function protocol(){try{state.hub=(await api('/v1/hub/protocol')).protocol;return state.hub}catch{return null}}

function renderNetwork(){state.online=navigator.onLine;$('#onlineText').textContent=state.online?'ONLINE':'OFFLINE';$('#onlinePill').classList.toggle('offline',!state.online);$('#deviceConnection').textContent=state.online?'Online':'Offline';$('#syncStatus').textContent=state.online?'● Recepção disponível':'● Fila local';}
window.addEventListener('online',()=>{renderNetwork();toast('Conexão restaurada. A fila NÃO foi enviada automaticamente.');health()});
window.addEventListener('offline',()=>{renderNetwork();toast('Modo offline. Mensagens pendentes continuam somente no aparelho.');});

const views={inicio:'Início',chamados:'Chamados',conversa:'Conversa',aprovacoes:'Aprovações',relatorios:'Relatórios',perfil:'Perfil'};
let currentView='inicio';
function goto(view){currentView=view;$$('.tab').forEach(b=>b.classList.toggle('active',b.dataset.view===view));$('#viewInicio').hidden=view!=='inicio';$('#moduleView').hidden=view==='inicio';if(view!=='inicio'){$('#moduleTitle').textContent=views[view]||view;renderCurrent()}}
$$('.tab').forEach(b=>b.addEventListener('click',()=>goto(b.dataset.view)));$('#backBtn')?.addEventListener('click',()=>goto('inicio'));$$('[data-view]').filter(x=>!x.classList.contains('tab')).forEach(b=>b.addEventListener('click',()=>goto(b.dataset.view)));

function renderBadges(){const all=state.messages.map(msgEnv);const tickets=all.filter(m=>['SUPPORT_INTAKE','SUPPORT_REQUEST','DIAGNOSTIC_SUMMARY','SUPPORT_STATUS'].includes(m.type)).length;const approvals=all.filter(m=>m.type==='PRIME_PROPOSAL').length;$('#badgeChamados').textContent=tickets;$('#badgeChamados').hidden=!tickets;$('#badgeAprovacoes').textContent=approvals;$('#badgeAprovacoes').hidden=!approvals}
async function renderOutboxCount(){const q=await get('hub.outbox')||[];$('#outboxCount').textContent=q.length;$('#outboxPill').classList.toggle('warn',q.length>0)}

function renderTickets(){
 const msgs=state.messages.map(msgEnv).filter(m=>['SUPPORT_INTAKE','SUPPORT_REQUEST','DIAGNOSTIC_SUMMARY','SUPPORT_STATUS'].includes(m.type));
 $('#moduleBody').innerHTML=`<div class="module-toolbar"><button id="syncTickets" class="primary">Atualizar do Hub</button><span>${msgs.length} item(ns)</span></div>${msgs.length?`<div class="ticket-list">${msgs.map(m=>`<button class="ticket-row ${msgPriority(m)}" data-msg="${esc(m.messageId)}"><div><strong>${esc(ticketId(m))}</strong><span>${esc(labelType(m.type))} • ${esc(m.sourceRole)}</span></div><b>${esc(summary(m))}</b><small>${new Date(m.createdAt).toLocaleString('pt-BR')}</small></button>`).join('')}</div>`:'<div class="empty"><b>Nenhum chamado pendente</b><span>Toque em “Atualizar do Hub” para consultar o Gateway.</span></div>'}`;
 $('#syncTickets')?.addEventListener('click',()=>pollHub());$$('[data-msg]').forEach(b=>b.addEventListener('click',()=>openMessage(b.dataset.msg)));
}
function openMessage(id){const item=state.messages.find(x=>msgEnv(x).messageId===id);if(!item)return;state.selected=msgEnv(item);goto('conversa')}
function conversationMessages(){const sel=state.selected;if(!sel)return [];const cor=sel.correlationId;return state.messages.map(msgEnv).filter(m=>m.correlationId===cor||m.messageId===sel.messageId)}
function renderConversation(){
 const sel=state.selected, list=conversationMessages();
 $('#moduleBody').innerHTML=`${sel?`<div class="thread-head"><div><small>${esc(ticketId(sel))}</small><h3>${esc(summary(sel))}</h3><span>${esc(sel.sourceRole)} → MASTER</span></div><button id="ackThread">Marcar recebida</button></div>`:'<div class="empty compact"><b>Selecione um chamado</b><span>Abra um item em Chamados ou envie uma nova mensagem à NEXA.</span></div>'}<div class="chat-log">${list.map(m=>`<article class="bubble ${m.sourceRole==='MASTER'?'mine':''}"><small>${esc(m.sourceRole)} • ${esc(labelType(m.type))}</small><p>${esc(m.payload?.message||m.payload?.subject||summary(m))}</p><time>${new Date(m.createdAt).toLocaleString('pt-BR')}</time></article>`).join('')}</div><form id="replyForm" class="composer"><textarea id="replyText" maxlength="4000" placeholder="Digite uma resposta ou orientação técnica..."></textarea><div><button type="button" id="newToNexa">Nova mensagem à NEXA</button><button class="primary" type="submit">Enviar</button></div></form>`;
 $('#replyForm')?.addEventListener('submit',sendReply);$('#newToNexa')?.addEventListener('click',()=>{state.selected=null;$('#replyText').focus()});$('#ackThread')?.addEventListener('click',async()=>{if(sel){await ackMessages([sel.messageId]);state.messages=state.messages.filter(x=>msgEnv(x).messageId!==sel.messageId);state.selected=null;renderBadges();renderConversation();toast('Mensagem confirmada e removida da fila.')}})
}
async function sendReply(e){e.preventDefault();const text=$('#replyText').value.trim();if(!text)return;const sel=state.selected;const targetRole=sel?.sourceRole||'NEXA',targetNodeId=sel?.sourceNodeId||'';await sendEnvelope({targetRole,targetNodeId,type:'SUPPORT_MESSAGE',correlationId:sel?.correlationId||'',taskId:sel?.taskId||'',payload:{message:text,ticketId:sel?ticketId(sel):undefined,channel:'NEXA_MOBILE',manualSend:true}});$('#replyText').value='';toast('Resposta processada.');await renderOutboxCount()}

function renderApprovals(){
 const proposals=state.messages.map(msgEnv).filter(m=>m.type==='PRIME_PROPOSAL');
 $('#moduleBody').innerHTML=`<div class="module-toolbar"><button id="syncApprovals" class="primary">Atualizar aprovações</button><span>${proposals.length} pendente(s)</span></div>${proposals.length?`<div class="approval-list">${proposals.map(m=>`<article class="approval-item"><header><div><small>${esc(ticketId(m))}</small><h3>${esc(m.payload?.title||'Ação proposta')}</h3></div><span class="risk-chip">${esc(m.payload?.risk||'REVISAR')}</span></header><p>${esc(m.payload?.description||m.payload?.message||'A proposta exige decisão explícita do MASTER.')}</p><div class="meta"><span>Origem: ${esc(m.sourceRole)}</span><span>Hash: ${esc((m.payloadHash||'').slice(0,12))}…</span></div><div class="decision-row"><button class="deny" data-decision="DENY" data-id="${esc(m.messageId)}">Negar</button><button class="approve" data-decision="APPROVE" data-id="${esc(m.messageId)}">Aprovar</button></div></article>`).join('')}</div>`:'<div class="empty"><b>Nenhuma aprovação pendente</b><span>O Hub não possui PRIME_PROPOSAL para este aparelho.</span></div>'}`;
 $('#syncApprovals')?.addEventListener('click',()=>pollHub());$$('[data-decision]').forEach(b=>b.addEventListener('click',()=>decide(b.dataset.id,b.dataset.decision)));
}
async function decide(id,decision){const m=state.messages.map(msgEnv).find(x=>x.messageId===id);if(!m)return;await sendEnvelope({targetRole:m.sourceRole,targetNodeId:m.sourceNodeId,type:'PRIME_RESULT',correlationId:m.correlationId,taskId:m.taskId,payload:{decision,proposalMessageId:m.messageId,ticketId:ticketId(m),proposalHash:m.payloadHash,decidedAt:nowIso(),manualApproval:true}});await ackMessages([m.messageId]);state.messages=state.messages.filter(x=>msgEnv(x).messageId!==m.messageId);renderBadges();renderApprovals();toast(`${decision==='APPROVE'?'Aprovação':'Negação'} assinada e enviada.`)}

async function renderReports(){const q=await get('hub.outbox')||[];const h=state.health||{};const p=state.hub||{};$('#moduleBody').innerHTML=`<div class="report-grid"><article><small>Gateway</small><b>${h.ok?'ONLINE':'SEM DADOS'}</b><span>${esc(h.version||'—')}</span></article><article><small>Communication Hub</small><b>${p.protocolVersion||'—'}</b><span>${p.persistence||'—'}</span></article><article><small>Fila local</small><b>${q.length}</b><span>envio manual</span></article><article><small>Última consulta</small><b>${state.lastSync?new Date(state.lastSync).toLocaleTimeString('pt-BR'):'—'}</b><span>${state.lastSync?new Date(state.lastSync).toLocaleDateString('pt-BR'):'não consultado'}</span></article></div><div class="report-actions"><button id="refreshReport" class="primary">Atualizar diagnóstico</button><button id="exportReport">Exportar relatório JSON</button></div><pre class="technical">${esc(JSON.stringify({online:navigator.onLine,standalone:matchMedia('(display-mode: standalone)').matches||navigator.standalone===true,platform:navigator.platform,gateway:{ok:h.ok,version:h.version,compatibility_id:h.compatibility_id},hub:{protocolVersion:p.protocolVersion,persistence:p.persistence,nodeTargeting:p.nodeTargeting,remoteMutation:p.remoteMutation,autoSendOnReconnect:p.autoSendOnReconnect},session:state.session?{nodeId:state.session.nodeId,role:state.session.role,expiresAt:state.session.expiresAt}:null,pendingLocal:q.length},null,2))}</pre>`;$('#refreshReport')?.addEventListener('click',async()=>{await Promise.all([health(),protocol(),pollHub({quiet:true})]);renderReports();toast('Diagnóstico atualizado.')});$('#exportReport')?.addEventListener('click',exportReport)}
async function exportReport(){const q=await get('hub.outbox')||[];const data={schema:'NEXO_MOBILE_TECH_REPORT_V1',generatedAt:nowIso(),online:navigator.onLine,gateway:state.health,hub:state.hub,device:state.session?{nodeId:state.session.nodeId,role:state.session.role,expiresAt:state.session.expiresAt}:null,pendingLocal:q.length};const blob=new Blob([JSON.stringify(data,null,2)],{type:'application/json'}),a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download=`NEXO_MOBILE_REPORT_${Date.now()}.json`;a.click();setTimeout(()=>URL.revokeObjectURL(a.href),1000)}

async function renderProfile(){const s=state.session||await loadSession(), iid=await installId(), q=await get('hub.outbox')||[];if(currentView!=='perfil')return;$('#moduleBody').innerHTML=`<div class="profile-card"><div class="profile-avatar">EV</div><div><h3>MASTER Mobile</h3><p>iPhone/PWA vinculado ao NEXO Communication Hub.</p></div></div><dl class="facts"><div><dt>Status</dt><dd>${s?'ATIVO':'NÃO ATIVADO'}</dd></div><div><dt>Node ID</dt><dd>${esc(s?.nodeId||'—')}</dd></div><div><dt>Install ID</dt><dd>${esc(iid)}</dd></div><div><dt>Expira</dt><dd>${s?new Date(s.expiresAt).toLocaleString('pt-BR'):'—'}</dd></div><div><dt>Fila local</dt><dd>${q.length}</dd></div></dl><div class="profile-actions"><button id="activatePhone" class="primary">${s?'Renovar credencial':'Ativar este iPhone'}</button><button id="flushOutbox">Enviar pendentes agora</button><button id="resetPhone" class="danger-outline">Remover credencial local</button></div><p class="privacy-note">A chave privada P-256 é criada como não exportável pelo WebCrypto. Reconectar à internet não envia automaticamente a fila local.</p>`;$('#activatePhone')?.addEventListener('click',async()=>{try{await enrollHub();renderProfile()}catch(e){toast('Ativação falhou: '+e.message)}});$('#flushOutbox')?.addEventListener('click',flushOutbox);$('#resetPhone')?.addEventListener('click',resetEnrollment)}

function renderCurrent(){if(currentView==='chamados')renderTickets();else if(currentView==='conversa')renderConversation();else if(currentView==='aprovacoes')renderApprovals();else if(currentView==='relatorios')renderReports();else if(currentView==='perfil')renderProfile()}

async function runLocalDiagnostic(){const report={online:navigator.onLine,webcrypto:Boolean(crypto?.subtle),indexedDB:Boolean(indexedDB),standalone:matchMedia('(display-mode: standalone)').matches||navigator.standalone===true,ios:/iPhone|iPad|iPod/.test(navigator.userAgent),gateway:Boolean(await health()),hub:Boolean(await protocol()),enrolled:Boolean(state.session||await loadSession())};toast(Object.values(report).filter(Boolean).length+'/'+Object.keys(report).length+' verificações locais OK');return report}
$('#diagnosticBtn')?.addEventListener('click',runLocalDiagnostic);$('#chatBtn')?.addEventListener('click',()=>goto('conversa'));$('#ticketBtn')?.addEventListener('click',()=>goto('chamados'));$('#syncNow')?.addEventListener('click',()=>pollHub());$('#flushNow')?.addEventListener('click',flushOutbox);$('#refreshStatus')?.addEventListener('click',async()=>{await Promise.all([health(),protocol(),pollHub({quiet:true})]);toast('Status atualizado.')});

async function boot(){renderNetwork();await loadSession();state.messages=await get('hub.inbox')||[];await Promise.all([health(),protocol(),renderOutboxCount()]);renderBadges();if(state.session&&navigator.onLine)pollHub({quiet:true});setInterval(()=>{if(state.session&&navigator.onLine)pollHub({quiet:true})},30000);if('serviceWorker'in navigator)navigator.serviceWorker.register('./sw.js').catch(()=>{});}
boot();
