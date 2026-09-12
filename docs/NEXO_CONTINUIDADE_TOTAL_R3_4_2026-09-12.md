# NEXO — CHECKPOINT DE CONTINUIDADE R3.4 LAB

Data: 12/09/2026

## Escopo oficial — 5 produtos
1. Cliente / PDV + ERP Windows
2. MASTER Windows
3. Support / NEXA Windows
4. NEXA Mobile / iPhone PWA
5. PDV ERP Mobile

## Baselines
- Cliente: R16.2 Parte 7 + VISUAL R3.4 LAB
- MASTER: 0.16 + VISUAL R3.4 LAB
- NEXA: R12 + VISUAL R3.4 LAB
- Mobile: branch `nexo-suite-r34-lab`

## Identidade visual canônica
- background `#050b12`
- surface `#0b1824`
- surface2 `#0d1d2a`
- blue `#2c9de7`
- success `#21c99a`
- warning `#e5ac4c`
- danger `#e56572`
- text `#f5f9ff`

## Gates desktop
- Cliente baseline: 230/230 PASS
- Cliente R3.4: 6/6 PASS
- MASTER combinado: 16/16 PASS
- NEXA combinado: 40/40 PASS
- identidade cruzada: PASS

## Regras permanentes
- não usar screenshot como interface;
- preservar banco/dados reais;
- Cliente mantém NEXO_Core.exe intacto;
- MASTER em modo interno sem MFA, preservando RBAC/sessão/cofre/auditoria;
- NEXA evidence-first;
- Hub ECDSA P-256 + targetNodeId + safeStorage + remoteMutation:false;
- reconnect não autoenvia outbox/conversas/diagnósticos;
- nenhuma alteração de produção/Railway sem autorização explícita.

## Mobile R3.4
Branch: `nexo-suite-r34-lab`

NEXA Mobile:
- `gateway/public/mobile/suite-r34.css`
- runtime CSS carrega R3.3 e depois R3.4

PDV ERP Mobile:
- `gateway/public/pdv-mobile/suite-r34.css`
- runtime CSS carrega R3.3 e depois R3.4
- IndexedDB/carrinho/outbox preservados
- push/pull/ACK central ainda NOT IMPLEMENTED

## Bloqueadores comerciais
1. Hub central EPHEMERAL_LAB
2. sync central PDV Mobile
3. Windows/iPhone físicos
4. hardware/fiscal/TEF/SEFAZ
5. Authenticode

## Próximo passo
1. confirmar CI mais recente da branch R3.4;
2. validar visual físico Windows/iPhone;
3. corrigir divergências reais;
4. decidir build Windows LAB;
5. não promover comercial enquanto houver P1.
