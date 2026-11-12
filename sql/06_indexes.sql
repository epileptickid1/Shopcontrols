-- B-tree sales(sold_at) and Hash sales(customer_id) already come from store.sql.
CREATE INDEX ix_payments_sale ON store.payments(sale_id);
CREATE INDEX ix_inventory_product ON store.inventory(product_id);
CREATE INDEX ix_products_active_category ON store.products(category_id) WHERE deleted_at IS NULL;
-- numeric(p,s) permits NaN, so explicitly reject non-finite inputs.
DO $$
DECLARE r record;
BEGIN
 FOR r IN SELECT table_name,column_name FROM information_schema.columns
 WHERE table_schema='store' AND data_type='numeric' AND table_name IN
 (SELECT tablename FROM pg_tables WHERE schemaname='store') LOOP
  EXECUTE format('ALTER TABLE store.%I ADD CONSTRAINT %I CHECK (%I NOT IN (''NaN''::numeric,''Infinity''::numeric,''-Infinity''::numeric))',
   r.table_name,'ck_'||r.table_name||'_'||r.column_name||'_finite',r.column_name);
 END LOOP;
END $$;


