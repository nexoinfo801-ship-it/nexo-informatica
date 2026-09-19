import {MobileGatewayService} from './mobile_gateway.mjs';
import {MemoryMobileStore} from './mobile_store.mjs';

function sessionHeader(headers={}){return String(headers['x-nexo-mobile-session']||headers['X-Nexo-Mobile-Session']||'').trim();}
function errStatus(code){if(/NOT_FOUND/.test(code))return 404;if(/EXPIRED|INVALID|REPLAY|REJECTED|REQUIRED|MISMATCH|FIELDS|PRIORITY/.test(code))return 400;if(/SCOPE/.test(code))return 403;if(/TIMEOUT/.test(code))return 504;return 500;}
export function createMobileHttpApi({hub,store=null,service=null}={}){
  if(!hub)throw new Error('MOBILE_HTTP_HUB_REQUIRED');
  const backing=store||new MemoryMobileStore();
  const mobile=service||new MobileGatewayService({store:backing,hub});
  return{
    async ready(){return backing.init?backing.init():{ok:true};},
    async close(){return backing.close?.();},
    protocol(){return mobile.protocol();},
    async handle({method='GET',pathname='/',headers={},body={}}={}){
      method=String(method).toUpperCase();
      try{
        if(method==='GET'&&pathname==='/v1/mobile/protocol')return{handled:true,status:200,body:{ok:true,protocol:mobile.protocol()}};
        if(method==='POST'&&pathname==='/v1/mobile/pair/create'){
          const auth=hub.verifyNodeRequest(body,{schema:'NEXO_MOBILE_PAIR_CREATE_V1',role:'NEXA'});
          const pairing=await mobile.createPairingFromNexa({nexaNodeId:auth.nodeId,requestedScopes:body.request?.scopes,label:body.request?.label});
          return{handled:true,status:200,body:{ok:true,pairing}};
        }
        if(method==='POST'&&pathname==='/v1/mobile/pair/claim')return{handled:true,status:200,body:{ok:true,session:await mobile.claimPairing(body)}};
        if(method==='POST'&&pathname==='/v1/mobile/bridge/heartbeat'){
          const auth=hub.verifyNodeRequest(body,{schema:'NEXO_MOBILE_BRIDGE_HEARTBEAT_V1',role:'NEXA'});
          return{handled:true,status:200,body:{ok:true,presence:await mobile.heartbeatFromNexa(auth.nodeId)}};
        }
        const token=sessionHeader(headers);
        if(method==='POST'&&pathname==='/v1/mobile/support/ask')return{handled:true,status:200,body:await mobile.ask(token,body)};
        if(method==='GET'&&pathname==='/v1/mobile/support/tickets')return{handled:true,status:200,body:{ok:true,tickets:await mobile.listTickets(token)}};
        if(method==='POST'&&pathname==='/v1/mobile/support/tickets')return{handled:true,status:201,body:{ok:true,ticket:await mobile.createTicket(token,body)}};
        if(method==='GET'&&pathname==='/v1/mobile/status')return{handled:true,status:200,body:{ok:true,status:await mobile.status(token)}};
        return{handled:false};
      }catch(error){const code=String(error?.message||'MOBILE_INTERNAL_ERROR');return{handled:true,status:errStatus(code),body:{ok:false,error:code}};}
    }
  };
}