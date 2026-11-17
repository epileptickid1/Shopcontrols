CREATE SCHEMA IF NOT EXISTS __SCHEMA__;

CREATE TABLE IF NOT EXISTS __SCHEMA__.receipts (
    sale_id text PRIMARY KEY,
    receipt_number text COLLATE "C" NOT NULL UNIQUE,
    sold_at timestamptz(3) NOT NULL,
    total_minor bigint NOT NULL CHECK (total_minor >= 0)
);

CREATE TABLE IF NOT EXISTS __SCHEMA__.receipt_items (
    sale_id text NOT NULL REFERENCES __SCHEMA__.receipts,
    position integer NOT NULL CHECK (position > 0),
    product_id text NOT NULL,
    product_name text NOT NULL,
    department_id text NOT NULL,
    department_name text NOT NULL,
    quantity_milli bigint NOT NULL CHECK (quantity_milli > 0),
    unit_price_minor bigint NOT NULL CHECK (unit_price_minor >= 0),
    discount_minor bigint NOT NULL CHECK (discount_minor >= 0),
    line_total_minor bigint NOT NULL CHECK (line_total_minor >= 0),
    PRIMARY KEY (sale_id, position)
);

CREATE TABLE IF NOT EXISTS __SCHEMA__.receipt_payments (
    sale_id text NOT NULL REFERENCES __SCHEMA__.receipts,
    position integer NOT NULL CHECK (position > 0),
    method text COLLATE "C" NOT NULL CHECK (method IN ('cash', 'card', 'bank_transfer')),
    amount_minor bigint NOT NULL CHECK (amount_minor > 0),
    PRIMARY KEY (sale_id, position)
);

CREATE INDEX IF NOT EXISTS receipts_sold_at_idx
    ON __SCHEMA__.receipts (sold_at DESC, receipt_number);
CREATE INDEX IF NOT EXISTS receipt_items_product_idx
    ON __SCHEMA__.receipt_items (product_id, sale_id);
