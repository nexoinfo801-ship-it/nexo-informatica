BEGIN;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS nexo_companies (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name text NOT NULL,
  status text NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE','BLOCKED')),
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS nexo_licenses (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  company_id uuid NOT NULL REFERENCES nexo_companies(id),
  serial text NOT NULL UNIQUE,
  license_key_hash char(64) NOT NULL UNIQUE CHECK (license_key_hash ~ '^[0-9a-f]{64}$'),
  status text NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE','REVOKED','SUSPENDED','TRANSFERRED','CLIENT_BLOCKED','ACTIVATION_BLOCKED')),
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS nexo_activations (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  license_id uuid NOT NULL REFERENCES nexo_licenses(id) ON DELETE CASCADE,
  install_id text NOT NULL,
  status text NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE','REVOKED','SUSPENDED','TRANSFERRED','CLIENT_BLOCKED','ACTIVATION_BLOCKED')),
  status_seq bigint NOT NULL DEFAULT 1 CHECK (status_seq > 0),
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE(license_id, install_id)
);

CREATE TABLE IF NOT EXISTS nexo_support_tickets (
  id uuid PRIMARY KEY,
  protocol text NOT NULL UNIQUE,
  company_id uuid NOT NULL REFERENCES nexo_companies(id),
  license_id uuid NOT NULL REFERENCES nexo_licenses(id),
  activation_id uuid NOT NULL REFERENCES nexo_activations(id),
  install_id text NOT NULL,
  local_protocol text NOT NULL DEFAULT '',
  category text NOT NULL,
  priority text NOT NULL,
  subject text NOT NULL,
  description text NOT NULL,
  status text NOT NULL DEFAULT 'Aberto',
  owner text NOT NULL DEFAULT '',
  diagnostic jsonb NOT NULL DEFAULT '{}'::jsonb,
  notifications jsonb NOT NULL DEFAULT '{}'::jsonb,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  closed_at timestamptz
);

CREATE TABLE IF NOT EXISTS nexo_support_messages (
  id uuid PRIMARY KEY,
  ticket_id uuid NOT NULL REFERENCES nexo_support_tickets(id) ON DELETE CASCADE,
  company_id uuid NOT NULL REFERENCES nexo_companies(id),
  author_type text NOT NULL CHECK (author_type IN ('client','central','system')),
  text text NOT NULL CHECK (char_length(text) BETWEEN 1 AND 8000),
  request_id text NOT NULL UNIQUE,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS nexo_idempotency (
  request_id text PRIMARY KEY,
  company_id uuid NOT NULL REFERENCES nexo_companies(id),
  license_id uuid NOT NULL REFERENCES nexo_licenses(id),
  install_id text NOT NULL,
  action text NOT NULL,
  response_json jsonb NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  expires_at timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS nexo_license_acks (
  id bigserial PRIMARY KEY,
  company_id uuid NOT NULL REFERENCES nexo_companies(id),
  license_id uuid NOT NULL REFERENCES nexo_licenses(id),
  activation_id uuid NOT NULL REFERENCES nexo_activations(id),
  install_id text NOT NULL,
  state text NOT NULL,
  client_time timestamptz,
  request_id text NOT NULL UNIQUE,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS nexo_audit_events (
  id bigserial PRIMARY KEY,
  company_id uuid REFERENCES nexo_companies(id),
  install_id text,
  action text NOT NULL,
  entity_type text,
  entity_id text,
  request_id text,
  outcome text NOT NULL DEFAULT 'OK',
  meta jsonb NOT NULL DEFAULT '{}'::jsonb,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_nexo_activation_install ON nexo_activations(install_id);
CREATE INDEX IF NOT EXISTS idx_nexo_ticket_scope ON nexo_support_tickets(company_id,license_id,install_id,updated_at DESC);
CREATE INDEX IF NOT EXISTS idx_nexo_message_ticket ON nexo_support_messages(ticket_id,created_at);
CREATE INDEX IF NOT EXISTS idx_nexo_audit_scope ON nexo_audit_events(company_id,created_at DESC);
CREATE INDEX IF NOT EXISTS idx_nexo_idempotency_scope ON nexo_idempotency(company_id,license_id,install_id,expires_at);
CREATE INDEX IF NOT EXISTS idx_nexo_idempotency_expiry ON nexo_idempotency(expires_at);
COMMIT;
