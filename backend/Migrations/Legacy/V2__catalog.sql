CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE categories (
  id UUID PRIMARY KEY,
  name VARCHAR(100) NOT NULL UNIQUE,
  slug VARCHAR(120) NOT NULL UNIQUE,
  active BOOLEAN NOT NULL DEFAULT TRUE,
  sort_order INTEGER NOT NULL DEFAULT 0,
  created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
  deleted_at TIMESTAMPTZ
);

CREATE TABLE products (
  id UUID PRIMARY KEY,
  category_id UUID NOT NULL REFERENCES categories(id),
  name VARCHAR(160) NOT NULL,
  slug VARCHAR(180) NOT NULL UNIQUE,
  search_name VARCHAR(180) NOT NULL,
  description VARCHAR(500),
  image_url VARCHAR(500),
  base_price BIGINT NOT NULL CHECK (base_price >= 0),
  sale_price BIGINT CHECK (sale_price IS NULL OR sale_price >= 0),
  available BOOLEAN NOT NULL DEFAULT TRUE,
  featured BOOLEAN NOT NULL DEFAULT FALSE,
  best_seller BOOLEAN NOT NULL DEFAULT FALSE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
  deleted_at TIMESTAMPTZ
);
CREATE INDEX idx_products_catalog ON products(category_id, available) WHERE deleted_at IS NULL;
CREATE INDEX idx_products_search ON products(search_name) WHERE available = TRUE AND deleted_at IS NULL;

CREATE TABLE product_sizes (
  id UUID PRIMARY KEY,
  product_id UUID NOT NULL REFERENCES products(id),
  name VARCHAR(20) NOT NULL,
  price_delta BIGINT NOT NULL DEFAULT 0,
  available BOOLEAN NOT NULL DEFAULT TRUE,
  UNIQUE(product_id, name)
);
CREATE TABLE toppings (
  id UUID PRIMARY KEY,
  name VARCHAR(100) NOT NULL UNIQUE,
  price BIGINT NOT NULL CHECK (price >= 0),
  image_url VARCHAR(500),
  available BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE product_toppings (
  product_id UUID NOT NULL REFERENCES products(id),
  topping_id UUID NOT NULL REFERENCES toppings(id),
  PRIMARY KEY(product_id, topping_id)
);

INSERT INTO categories (id, name, slug, sort_order) VALUES
('10000000-0000-0000-0000-000000000001','Trà sữa','tra-sua',1),
('10000000-0000-0000-0000-000000000002','Trà hoa quả','tra-hoa-qua',2),
('10000000-0000-0000-0000-000000000003','Cà phê','ca-phe',3),
('10000000-0000-0000-0000-000000000004','Sinh tố','sinh-to',4),
('10000000-0000-0000-0000-000000000005','Nước ép','nuoc-ep',5);

INSERT INTO products (id, category_id, name, slug, search_name, description, base_price, sale_price, available, featured, best_seller) VALUES
('20000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001','Trà sữa truyền thống','tra-sua-truyen-thong','tra sua truyen thong','Vị trà thơm, béo vừa và dễ uống.',25000,NULL,TRUE,TRUE,TRUE),
('20000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000001','Trà sữa matcha','tra-sua-matcha','tra sua matcha','Matcha dịu, thơm sữa.',30000,NULL,TRUE,TRUE,TRUE),
('20000000-0000-0000-0000-000000000003','10000000-0000-0000-0000-000000000001','Trà sữa đường đen','tra-sua-duong-den','tra sua duong den','Trân châu đường đen mềm dẻo.',32000,28000,TRUE,FALSE,TRUE),
('20000000-0000-0000-0000-000000000004','10000000-0000-0000-0000-000000000002','Trà đào cam sả','tra-dao-cam-sa','tra dao cam sa','Mát lành, thơm đào và sả.',35000,NULL,TRUE,TRUE,TRUE),
('20000000-0000-0000-0000-000000000005','10000000-0000-0000-0000-000000000002','Trà chanh','tra-chanh','tra chanh','Chua ngọt vừa vị.',18000,NULL,TRUE,FALSE,FALSE),
('20000000-0000-0000-0000-000000000006','10000000-0000-0000-0000-000000000002','Trà quất mật ong','tra-quat-mat-ong','tra quat mat ong','Vị quất tươi cùng mật ong.',22000,NULL,TRUE,FALSE,FALSE),
('20000000-0000-0000-0000-000000000007','10000000-0000-0000-0000-000000000003','Cà phê đen','ca-phe-den','ca phe den','Đậm vị cà phê Việt.',18000,NULL,TRUE,FALSE,TRUE),
('20000000-0000-0000-0000-000000000008','10000000-0000-0000-0000-000000000003','Cà phê sữa','ca-phe-sua','ca phe sua','Cà phê pha phin cùng sữa đặc.',22000,NULL,TRUE,TRUE,TRUE),
('20000000-0000-0000-0000-000000000009','10000000-0000-0000-0000-000000000003','Bạc xỉu','bac-xiu','bac xiu','Nhẹ nhàng, nhiều sữa.',25000,NULL,TRUE,FALSE,FALSE),
('20000000-0000-0000-0000-000000000010','10000000-0000-0000-0000-000000000004','Sinh tố bơ','sinh-to-bo','sinh to bo','Bơ tươi xay mịn.',35000,NULL,TRUE,TRUE,FALSE),
('20000000-0000-0000-0000-000000000011','10000000-0000-0000-0000-000000000004','Sinh tố xoài','sinh-to-xoai','sinh to xoai','Xoài chín ngọt thơm.',32000,NULL,TRUE,FALSE,FALSE),
('20000000-0000-0000-0000-000000000012','10000000-0000-0000-0000-000000000005','Nước ép cam','nuoc-ep-cam','nuoc ep cam','Cam tươi ép mỗi ngày.',28000,NULL,TRUE,FALSE,FALSE),
('20000000-0000-0000-0000-000000000013','10000000-0000-0000-0000-000000000005','Nước ép ổi','nuoc-ep-oi','nuoc ep oi','Ổi tươi thanh mát.',28000,NULL,TRUE,FALSE,FALSE),
('20000000-0000-0000-0000-000000000014','10000000-0000-0000-0000-000000000001','Trà sữa khoai môn','tra-sua-khoai-mon','tra sua khoai mon','Hương khoai môn thơm bùi.',30000,NULL,TRUE,FALSE,FALSE),
('20000000-0000-0000-0000-000000000015','10000000-0000-0000-0000-000000000002','Hồng trà sữa','hong-tra-sua','hong tra sua','Hồng trà thơm nồng, sữa béo vừa.',27000,NULL,TRUE,FALSE,FALSE);

INSERT INTO product_sizes (id, product_id, name, price_delta) SELECT gen_random_uuid(), id, 'M', 0 FROM products;
INSERT INTO product_sizes (id, product_id, name, price_delta) SELECT gen_random_uuid(), id, 'L', 5000 FROM products;
INSERT INTO toppings (id, name, price, available) VALUES
('30000000-0000-0000-0000-000000000001','Trân châu đen',5000,TRUE),
('30000000-0000-0000-0000-000000000002','Trân châu trắng',5000,TRUE),
('30000000-0000-0000-0000-000000000003','Thạch trái cây',5000,TRUE),
('30000000-0000-0000-0000-000000000004','Pudding trứng',7000,TRUE),
('30000000-0000-0000-0000-000000000005','Kem cheese',8000,TRUE);
INSERT INTO product_toppings(product_id, topping_id) SELECT p.id, t.id FROM products p CROSS JOIN toppings t WHERE p.category_id IN ('10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000002');
