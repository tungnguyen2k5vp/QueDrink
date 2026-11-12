CREATE TABLE orders (
  id UUID PRIMARY KEY,
  order_code VARCHAR(32) NOT NULL UNIQUE,
  guest_name VARCHAR(120) NOT NULL,
  guest_phone VARCHAR(20) NOT NULL,
  delivery_address VARCHAR(500) NOT NULL,
  note VARCHAR(500),
  subtotal BIGINT NOT NULL CHECK (subtotal >= 0),
  shipping_fee BIGINT NOT NULL CHECK (shipping_fee >= 0),
  total BIGINT NOT NULL CHECK (total >= 0),
  payment_method VARCHAR(32) NOT NULL,
  payment_status VARCHAR(32) NOT NULL,
  order_status VARCHAR(32) NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX idx_orders_lookup ON orders(order_code, guest_phone);
CREATE TABLE order_items (
  id UUID PRIMARY KEY,
  order_id UUID NOT NULL REFERENCES orders(id),
  product_id UUID REFERENCES products(id),
  product_name VARCHAR(160) NOT NULL,
  unit_price BIGINT NOT NULL CHECK (unit_price >= 0),
  quantity INTEGER NOT NULL CHECK (quantity > 0 AND quantity <= 20),
  line_total BIGINT NOT NULL CHECK (line_total >= 0)
);
