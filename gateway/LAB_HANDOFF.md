# NEXO Gateway — LAB Handoff

## Estado

- Branch de laboratório: `nexo-db-integration-lab`
- Branch base preservada: `nexo-gateway-prep`
- Projeto Railway preservado: `NEXO Gateway`
- Serviço preservado: `gateway`
- Domínio canônico: `nexo.sideproject.cyou`
- Compatibilidade: `NEXO-SUITE-PRIME-R8-P5-MOBILE-20260911`
- Banco: projeto Neon `NEXO ERP PRO Cloud R16`

## Alterações LAB

- `db.mjs` adiciona o adaptador PostgreSQL/Neon.
- `server.mjs` expõe o estado de conectividade do banco em `/health`.
- `package.json` inclui o driver PostgreSQL.
- Teste de falha segura adicionado em `test/db_adapter.test.mjs`.
- A variável `DATABASE_URL` foi preparada no serviço Railway sem redeploy.
- Nenhuma chave privada ou valor secreto está neste arquivo.

## Variáveis necessárias no deploy

- `DATABASE_URL`
- `PUBLIC_GATEWAY_URL`
- `PUBLIC_SUPPORT_URL`
- `PUBLIC_API_URL`
- `BOOTSTRAP_SIGNING_PRIVATE_KEY_PEM_B64`
- `BOOTSTRAP_SIGNING_PUBLIC_JWK`
- `BOOTSTRAP_VERSION`
- `NEXO_UPSTREAM_URL` somente quando a Central real estiver pronta
- `MOBILE_LAB_ENABLED` somente para homologação mobile LAB

## Ordem de fechamento

1. Executar o build/teste do Dockerfile na branch LAB.
2. Validar `/health` com banco configurado.
3. Implementar as rotas autenticadas de sync usando `sync_events`, `sync_entity_state` e `device_sync_cursors`.
4. Ligar Cliente, PDV, MASTER, NEXA e Painel a essas rotas.
5. Testar idempotência, offline/online e reinício do Gateway.
6. Testar em redes diferentes.
7. Somente depois abrir PR ou promover para deploy.

## Limites

- Não usar a branch LAB como comercial.
- Não marcar testes Windows/iPhone/periféricos como PASS sem teste físico.
- Não realizar SQL destrutivo automaticamente.
- Não expor `DATABASE_URL` nem chaves.
