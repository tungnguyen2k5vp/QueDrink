ALTER TABLE orders
  ADD COLUMN delivery_type VARCHAR(20) NOT NULL DEFAULT 'DELIVERY',
  ADD COLUMN pickup_location VARCHAR(200),
  ADD COLUMN discount BIGINT NOT NULL DEFAULT 0 CHECK (discount >= 0),
  ADD COLUMN voucher_code VARCHAR(40),
  ADD COLUMN idempotency_key VARCHAR(80);
CREATE UNIQUE INDEX uq_orders_idempotency_key ON orders(idempotency_key) WHERE idempotency_key IS NOT NULL;

CREATE TABLE vouchers (
  id UUID PRIMARY KEY,
  code VARCHAR(40) NOT NULL UNIQUE,
  discount_type VARCHAR(20) NOT NULL CHECK (discount_type IN ('FIXED', 'PERCENT')),
  discount_value BIGINT NOT NULL CHECK (discount_value > 0),
  min_order BIGINT NOT NULL DEFAULT 0 CHECK (min_order >= 0),
  max_discount BIGINT,
  usage_limit INTEGER,
  used_count INTEGER NOT NULL DEFAULT 0,
  active BOOLEAN NOT NULL DEFAULT TRUE,
  starts_at TIMESTAMPTZ,
  ends_at TIMESTAMPTZ
);

INSERT INTO vouchers(id, code, discount_type, discount_value, min_order, max_discount, usage_limit)
VALUES
('70000000-0000-0000-0000-000000000001', 'WELCOME10', 'PERCENT', 10, 50000, 30000, 1000),
('70000000-0000-0000-0000-000000000002', 'QUE20K', 'FIXED', 20000, 120000, NULL, 500);

