DO $$
DECLARE t text;
BEGIN
  FOREACH t IN ARRAY ARRAY['departments','employees','users','categories','products','suppliers',
    'product_suppliers','inventory','supplies','customers','sales'] LOOP
    EXECUTE format('CREATE TRIGGER %I BEFORE INSERT OR UPDATE ON store.%I FOR EACH ROW EXECUTE FUNCTION store.fn_audit()', 'tr_'||t||'_audit', t);
  END LOOP;
  FOREACH t IN ARRAY ARRAY['departments','roles','employees','users','categories','products','suppliers',
    'product_suppliers','inventory','supplies','customers','sales'] LOOP
    EXECUTE format('CREATE TRIGGER %I BEFORE DELETE ON store.%I FOR EACH ROW EXECUTE FUNCTION store.fn_no_delete()', 'tr_'||t||'_no_delete', t);
  END LOOP;
  FOREACH t IN ARRAY ARRAY['departments','roles','employees','users','categories','products','suppliers',
    'supplies','supply_items','customers','sales','sale_items','payments'] LOOP
    EXECUTE format('CREATE TRIGGER %I BEFORE UPDATE ON store.%I FOR EACH ROW EXECUTE FUNCTION store.fn_fixed_key(''id'')', 'tr_'||t||'_key', t);
  END LOOP;
  FOREACH t IN ARRAY ARRAY['employees','users','products','product_suppliers','inventory'] LOOP
    EXECUTE format('CREATE TRIGGER %I BEFORE INSERT OR UPDATE ON store.%I FOR EACH ROW EXECUTE FUNCTION store.fn_validate_catalog()', 'tr_'||t||'_validate', t);
  END LOOP;
END $$;
CREATE TRIGGER tr_inventory_key BEFORE UPDATE ON store.inventory
 FOR EACH ROW EXECUTE FUNCTION store.fn_fixed_key('department_id', 'product_id');
CREATE TRIGGER tr_product_suppliers_key BEFORE UPDATE ON store.product_suppliers
 FOR EACH ROW EXECUTE FUNCTION store.fn_fixed_key('product_id', 'supplier_id');
CREATE TRIGGER tr_sales_guard BEFORE INSERT OR UPDATE ON store.sales
 FOR EACH ROW EXECUTE FUNCTION store.fn_guard_document();
CREATE TRIGGER tr_supplies_guard BEFORE INSERT OR UPDATE ON store.supplies
 FOR EACH ROW EXECUTE FUNCTION store.fn_guard_document();
CREATE TRIGGER tr_sale_items_guard BEFORE INSERT OR UPDATE OR DELETE ON store.sale_items
 FOR EACH ROW EXECUTE FUNCTION store.fn_guard_detail();
CREATE TRIGGER tr_supply_items_guard BEFORE INSERT OR UPDATE OR DELETE ON store.supply_items
 FOR EACH ROW EXECUTE FUNCTION store.fn_guard_detail();
CREATE TRIGGER tr_payments_guard BEFORE INSERT OR UPDATE OR DELETE ON store.payments
 FOR EACH ROW EXECUTE FUNCTION store.fn_guard_detail();

