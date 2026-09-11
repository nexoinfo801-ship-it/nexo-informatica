# NEXO Central API Contract V1 — PREP

## Transporte

Gateway envia `POST` JSON para a Central com `x-nexo-request-id` e `x-nexo-gateway-auth`. O segundo header é um segredo server-to-server e nunca vai para o Cliente. Produção exige HTTPS entre Gateway e Central.

Campos base do Cliente: `action`, `product=NEXO_ERP_PRO`, `version`, `install_id`; ações autenticadas incluem `license_key`.

## Ações Cliente

### health
Sem autenticação. Resposta `ok`, `service`, `version`.

### support_create
Entrada: `ticket.local_protocol`, `category`, `priority`, `subject`, `description`, `diagnostic` sanitizado.
Saída: `ticket` com `id`, `protocol`, `status`, `messages`, `notifications`.

### support_status / support_sync
Retorna somente tickets do escopo autenticado `company + license + install_id`.

### support_message
Entrada: `ticket_id`, `message`. Idempotente por request id.

### support_client_close
Entrada: `ticket_id`. Fecha somente ticket do mesmo escopo.

### license_status
Retorna `status_envelope` no formato compacto ES256. O payload é `NEXO_LICENSE_STATUS` format_version 3 e deve ser assinado por chave de STATUS previamente delegada pelo MASTER e ancorada no runtime Cliente. Sem signer válido a Central falha fechada com `503 LICENSE_STATUS_SIGNER_NOT_CONFIGURED`.

### license_ack
Registra confirmação local de estado ativo/bloqueado sem conceder poderes de mutação ao Cliente.

## Persistência

PostgreSQL: companies, licenses, activations, support_tickets, support_messages, idempotency, license_acks e audit_events. Chave de licença armazenada apenas como SHA-256.

## Fora do escopo V1

- shell remoto;
- SQL arbitrário;
- comandos de estoque/financeiro/fiscal;
- distribuição da chave MASTER;
- atualização remota de binários;
- painel MASTER/NEXA administrativo (próxima etapa, com autenticação de serviço/RBAC separada).
