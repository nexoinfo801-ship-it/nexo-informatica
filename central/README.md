# NEXO Central 0.1.0 PREP

Backend server-side para a cadeia Cliente -> Gateway -> Central -> MASTER/NEXA.

## Estado

PREP/LAB. Não publicar como produção até: PostgreSQL real, chave de status delegada, DNS/HTTPS, E2E entre redes e gate do Guardião.

## Ações compatíveis com o Cliente

- `health`
- `support_create`
- `support_status` (`support_sync` alias)
- `support_message`
- `support_client_close`
- `license_status`
- `license_ack`

A Central autentica por `SHA-256(license_key)` + `install_id`; a chave de licença em texto puro não é armazenada no schema.

## Variáveis

Obrigatórias para execução real:

- `PORT`
- `DATABASE_URL`
- `PUBLIC_GATEWAY_URL=https://gateway.nexo.sideproject.cyou`
- `GATEWAY_SHARED_SECRET=<segredo aleatório exclusivo Gateway→Central>`

Licença remota assinada, somente após delegação MASTER:

- `LICENSE_STATUS_KID=lic-status-...`
- `LICENSE_STATUS_SIGNING_PRIVATE_KEY_PEM_B64=...`
- `LICENSE_CHECK_INTERVAL_SECONDS=60`

Nunca reutilizar a chave privada de bootstrap do Gateway como chave de status de licença.

## Banco

Aplicar `db/001_init.sql` no PostgreSQL antes do primeiro start. A ativação deve existir no banco antes de autenticar; não há auto-binding por “primeiro dispositivo visto”. Isso é intencional e fail-closed.

## Desenvolvimento sem banco externo

Somente teste/local: `ALLOW_MEMORY_STORE=1`. Não usar em produção porque tickets seriam perdidos no restart.

## Segurança

- instalação é derivada server-side de licença + activation;
- tickets são escopados por company/license/install_id;
- idempotência por `x-nexo-request-id`;
- mensagens deduplicadas por request id;
- payload limitado;
- Central exige autenticação do Gateway por segredo server-to-server;
- diagnóstico é sanitizado novamente no servidor;
- logs não devem incluir license_key, chaves privadas ou diagnóstico bruto;
- `license_status` retorna 503 até a chave delegada estar configurada.
