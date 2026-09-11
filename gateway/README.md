# NEXO Gateway — preparação de produção

Serviço público mínimo para descoberta/bootstrap do ecossistema NEXO e encaminhamento controlado para a Central NEXO.

## Configuração canônica de deploy

- Repositório: `nexoinfo801-ship-it/nexo-informatica`
- Branch: `nexo-gateway-prep`
- Root directory: `gateway`
- Dockerfile: `gateway/Dockerfile` (dentro do root configurado, `Dockerfile`)
- Start command: `npm start`
- Healthcheck: `GET /health`
- Porta: **somente** `process.env.PORT` fornecida pela plataforma; não usar fallback/fixa 3000/8080
- Node.js: `22.x`
- Ambiente: `NODE_ENV=production`

## Objetivos

- expor `/health` sem dados sensíveis;
- expor `/v1/bootstrap` com configuração assinada ECDSA P-256;
- manter Cliente, MASTER e NEXA independentes de IP local/fixo;
- aceitar chamadas do gateway somente em JSON e com limite de tamanho/rate limit;
- encaminhar ações para a Central real somente quando `NEXO_UPSTREAM_URL` estiver configurado;
- falhar fechado (`503`) enquanto a Central real não estiver configurada.

## Variáveis da plataforma

As chaves privadas e outros segredos ficam exclusivamente nas variáveis do ambiente de deploy. Não adicionar segredos ao GitHub.

- `PORT` — fornecida pela plataforma e obrigatória
- `PUBLIC_GATEWAY_URL`
- `PUBLIC_SUPPORT_URL`
- `PUBLIC_API_URL`
- `BOOTSTRAP_SIGNING_PRIVATE_KEY_PEM_B64` (segredo)
- `BOOTSTRAP_SIGNING_PUBLIC_JWK`
- `BOOTSTRAP_VERSION`
- `NEXO_UPSTREAM_URL` (definir somente quando a Central real estiver pronta em HTTPS)
- `MAX_BODY_BYTES`
- `RATE_LIMIT_WINDOW_MS`
- `RATE_LIMIT_MAX`

## Segurança

O gateway não armazena senha, token, chave de licença ou conteúdo de chamados. O corpo das requisições não é gravado em log. O serviço só encaminha para `NEXO_UPSTREAM_URL` quando esse endereço é HTTPS válido.

A chave privada de bootstrap não deve ser reutilizada como chave MASTER de licença, chave de status, chave de release ou selo digital.

O container não fixa/expoõe uma porta de aplicação: o processo escuta em `0.0.0.0:$PORT`.

## Estado atual

`PREP`: gateway alinhado ao deploy por Dockerfile, Node 22, healthcheck `/health` e porta dinâmica da plataforma. Publicação, domínio customizado/DNS e ligação ao `NEXO_UPSTREAM_URL` continuam como gates externos.
