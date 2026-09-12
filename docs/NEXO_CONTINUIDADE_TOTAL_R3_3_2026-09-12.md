# NEXO — MEMÓRIA OPERACIONAL / RETOMADA

Data de checkpoint: 12/09/2026

Este arquivo existe para que a continuidade do projeto NÃO dependa da memória de uma conversa.

## Escopo oficial atual — 5 produtos

1. NEXO Cliente / PDV + ERP Windows
2. NEXO MASTER Windows
3. NEXO Support / NEXA Windows
4. NEXA Mobile / iPhone PWA
5. PDV ERP Mobile

## Estado atual

### Cliente Windows
Baseline técnica oficial: **R16.2 Parte 7**
Visual atual: **R3.3 LAB**

Regras permanentes:
- `NEXO_Core.exe` preservado;
- não voltar ao bridge histórico;
- banco real preservado;
- offline-first;
- sem autoenvio ao reconectar;
- NEXA integrada como rail contextual;
- referências visuais são source of truth, nunca screenshots embutidos.

Gates R3.3 registrados:
- 230/230 baseline;
- 23/23 R3;
- 7/7 identidade por módulo;
- 12/12 R3.2;
- 20/20 R3.3;
- 15/15 layouts R3.3.

### MASTER Windows
Visual: **R3.3 LAB**

Decisão permanente: **modo interno sem MFA na jornada ativa**.

Preservar:
- login/sessão local;
- RBAC;
- safeStorage/cofre;
- licenciamento;
- selo digital;
- contratos;
- auditoria.

Correção obrigatória preservada:
`PAGES` deve ser declarado antes do uso e navegação inválida deve cair com segurança no Dashboard.

### NEXA Windows
Visual: **R3.3 LAB**

Preservar:
- Knowledge Engine;
- evidence-first;
- chamados;
- conversa;
- diagnóstico;
- Hub;
- execução remota bloqueada por padrão;
- envio manual;
- nenhuma resposta irrelevante do tipo “iFood” para problemas de caixa.

### NEXA Mobile / iPhone
Branch LAB: `nexo-suite-r33-lab`

A camada R3.3 foi aplicada em `gateway/public/mobile/suite-r33.css`.
Nenhum deploy Railway foi feito a partir da branch LAB.

### PDV ERP Mobile
Branch LAB: `nexo-suite-r33-lab`

A camada R3.3 foi aplicada em `gateway/public/pdv-mobile/suite-r33.css`.
Preserva IndexedDB v2, carrinho persistente, produtos/clientes/vendas/outbox e offline-first.

**Ainda falta sync central push/pull/ACK real.**

## Identidade visual canônica

- fundo: `#050b12`
- superfície: `#0b1824`
- superfície secundária: `#0d1d2a`
- azul/informação: `#2c9de7`
- verde/sucesso: `#21c99a`
- amarelo/atenção: `#e5ac4c`
- vermelho/erro: `#e56572`
- branco/texto: `#f5f9ff`

Direção:
- menos azul saturando a tela;
- verde apenas semântico;
- sombras discretas;
- bordas finas;
- 10–14px de raio;
- NEXA humana e integrada;
- nenhuma interface “mockup”.

## Gateway / integração

Compatibilidade: `NEXO-SUITE-PRIME-R8-P5-MOBILE-20260911`

Manter por compatibilidade até migração coordenada.

Protocolo: `1.1.0-lab`

Regras:
- roles CLIENTE / MASTER / NEXA;
- ECDSA P-256;
- `targetNodeId`;
- `safeStorage`;
- `remoteMutation:false`;
- `autoSendOnReconnect:false`;
- ACK;
- payload hash;
- execução remota arbitrária proibida.

## CI Mobile R3.3

GitHub Actions run `34693326908`: **28/28 PASS**.

## Bloqueadores comerciais conhecidos

1. Hub central ainda é `EPHEMERAL_LAB`.
2. PDV ERP Mobile ainda não tem push/pull/ACK central real.
3. Authenticode comercial pendente.
4. Gates físicos Windows/iPhone/hardware/fiscal ainda são externos.

## Próxima etapa oficial

**R3.4 LAB — refinamento cruzado dos cinco produtos**

Prioridades:
1. tipografia e escala;
2. espaçamento;
3. densidade;
4. topbars e sidebars;
5. cards;
6. tabelas;
7. estados empty/loading/error/success;
8. responsividade;
9. comparação visual módulo a módulo;
10. gate cruzado dos cinco produtos.

Não gerar EXE antes da revisão LAB.
Não alterar produção/Railway sem autorização explícita.
