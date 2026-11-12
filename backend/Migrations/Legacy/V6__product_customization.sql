CREATE TABLE sugar_options (
  id UUID PRIMARY KEY,
  product_id UUID NOT NULL REFERENCES products(id),
  name VARCHAR(40) NOT NULL,
  sort_order INTEGER NOT NULL DEFAULT 0,
  available BOOLEAN NOT NULL DEFAULT TRUE,
  UNIQUE(product_id, name)
);

CREATE TABLE ice_options (
  id UUID PRIMARY KEY,
  product_id UUID NOT NULL REFERENCES products(id),
  name VARCHAR(40) NOT NULL,
  sort_order INTEGER NOT NULL DEFAULT 0,
  available BOOLEAN NOT NULL DEFAULT TRUE,
  UNIQUE(product_id, name)
);

INSERT INTO sugar_options(id, product_id, name, sort_order)
SELECT gen_random_uuid(), p.id, option.name, option.sort_order
FROM products p
CROSS JOIN (VALUES ('100% đường', 1), ('70% đường', 2), ('50% đường', 3), ('30% đường', 4), ('Không đường', 5)) AS option(name, sort_order);

INSERT INTO ice_options(id, product_id, name, sort_order)
SELECT gen_random_uuid(), p.id, option.name, option.sort_order
FROM products p
CROSS JOIN (VALUES ('100% đá', 1), ('70% đá', 2), ('50% đá', 3), ('Ít đá', 4), ('Không đá', 5)) AS option(name, sort_order);

ALTER TABLE order_items
  ADD COLUMN size_name VARCHAR(20),
  ADD COLUMN sugar_name VARCHAR(40),
  ADD COLUMN ice_name VARCHAR(40),
  ADD COLUMN toppings_summary VARCHAR(500),
  ADD COLUMN item_note VARCHAR(300);

