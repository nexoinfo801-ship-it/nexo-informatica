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

const DEFAULT_GATEWAY='https://api.nexo.sideproject.cyou';
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
    qs('#gatewayStatus').textContent='● Gateway pendente';
    qs('#latencyText').textContent='Aguardando DNS / deploy';
    return null;
  }
}
checkGateway();qs('#refreshStatus').addEventListener('click',()=>checkGateway().then(()=>showToast('Status atualizado.')));

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

qs('#approveBtn').addEventListener('click',()=>showToast('LAB: aprovação real aguardará challenge assinado do MASTER.'));
qs('#denyBtn').addEventListener('click',()=>showToast('LAB: negação real aguardará challenge assinado do MASTER.'));

if('serviceWorker' in navigator) navigator.serviceWorker.register('./sw.js').catch(()=>{});

const composer=qs('#composer'), feed=qs('#conversationFeed'), input=qs('#messageInput');
function addBubble(text,kind){const el=document.createElement('div');el.className='bubble '+kind;el.textContent=text;feed.append(el);feed.scrollTop=feed.scrollHeight;}
composer?.addEventListener('submit',(e)=>{e.preventDefault();const value=input.value.trim();if(!value)return;addBubble(value,'user');input.value='';setTimeout(()=>addBubble('Entendi. Vou organizar o contexto, indicar as evidências necessárias e pedir sua confirmação antes de qualquer ação que altere dados.','assistant'),350)});
