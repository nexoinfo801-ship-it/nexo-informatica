# NEXO — CONTINUIDADE TOTAL R3.5 — 12/09/2026

## Cinco produtos oficiais
1. Cliente / PDV + ERP Windows
2. MASTER Windows
3. Support / NEXA Windows
4. NEXA Mobile / iPhone
5. PDV ERP Mobile

## Desktop atual para revisão
**R3.5 LAB / FIELD REVIEW**

Cliente:
- baseline técnica R16.2 Parte 7
- 230/230 + 3/3 PASS
- Core SHA preservado `17b8ac70dd825c9e2b3290889c0ada0feba951d096c639499197c4bbad1de015`
- NEXA rail refinado

MASTER:
- 20/20 R3.5
- bug `(audit || []).slice is not a function` corrigido
- payload de auditoria normalizado de `{status,events}`
- sem MFA no modo interno
- preservar RBAC/safeStorage/auditoria

NEXA:
- 45/45 PASS
- human-first
- evidência técnica recolhida por padrão
- `meu caixa não abre` => Caixa/PDV
- não mencionar iFood/marketplace quando irrelevante
- evidence-first e execução remota bloqueada

Package gate:
**26/26 PASS**

## Mobile

Permanece na linha R3.4 LAB:
- branch `nexo-suite-r34-lab`
- não publicar Railway sem autorização
- NEXA Mobile/iPhone preservado
- PDV ERP Mobile offline-first preservado
- sync central push/pull/ACK continua NÃO IMPLEMENTADO

## Bloqueadores comerciais
- Hub central EPHEMERAL_LAB
- PDV Mobile sem sync central push/pull/ACK
- Authenticode
- hardware/fiscal/TEF/SEFAZ
- validação física final

## Próxima ação

Rodar `NEXO_R3_5_VALIDAR_E_ABRIR.cmd` junto aos 3 ZIPs R3.5 no Windows.

Critérios mais importantes:
1. MASTER sem `slice is not a function`
2. NEXA human-first e código recolhido
3. Cliente rail NEXA legível
4. consistência visual dos três desktops

Se a revisão física passar, consolidar R3.5 como baseline visual candidata antes da próxima rodada.
