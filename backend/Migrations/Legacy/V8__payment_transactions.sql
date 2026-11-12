CREATE TABLE payment_transactions (
  id UUID PRIMARY KEY,
  order_id UUID NOT NULL REFERENCES orders(id),
  provider VARCHAR(32) NOT NULL,
  provider_reference VARCHAR(80) NOT NULL UNIQUE,
  amount BIGINT NOT NULL CHECK (amount > 0),
  status VARCHAR(20) NOT NULL,
  idempotency_key VARCHAR(80) NOT NULL UNIQUE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX idx_payment_transactions_order ON payment_transactions(order_id, created_at DESC);

