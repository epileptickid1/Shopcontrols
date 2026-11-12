-- Fixed search_path on privileged routines prevents name substitution.
CREATE FUNCTION store.fn_actor_id(p_roles text[] DEFAULT NULL) RETURNS bigint
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
DECLARE v_id bigint;
BEGIN
  SELECT u.id INTO v_id FROM store.users u
  JOIN store.roles r ON r.id = u.role_id
  LEFT JOIN store.employees e ON e.id = u.employee_id
  LEFT JOIN store.departments d ON d.id = e.department_id
  WHERE u.username = session_user AND u.deleted_at IS NULL
    AND (u.employee_id IS NULL OR (e.deleted_at IS NULL AND d.deleted_at IS NULL))
    AND (p_roles IS NULL OR r.name = 'system' OR r.name = ANY(p_roles));
  IF v_id IS NULL THEN
    RAISE EXCEPTION 'No active authorized user for database login %', session_user USING ERRCODE = '42501';
  END IF;
  RETURN v_id;
END $$;

CREATE FUNCTION store.fn_require_active(p_table text, p_id bigint) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
DECLARE v_id bigint;
BEGIN
  IF p_table NOT IN ('departments', 'employees', 'users', 'categories', 'products', 'suppliers', 'customers') THEN
    RAISE EXCEPTION 'Unsupported reference table';
  END IF;
  EXECUTE format('SELECT id FROM store.%I WHERE id = $1 AND deleted_at IS NULL FOR SHARE', p_table)
    INTO v_id USING p_id;
  IF v_id IS NULL THEN
    RAISE EXCEPTION 'Missing or inactive %.%', p_table, p_id USING ERRCODE = '23514';
  END IF;
  IF p_table = 'products' THEN
    PERFORM store.fn_require_active('categories', category_id) FROM store.products WHERE id = p_id;
  END IF;
END $$;

CREATE FUNCTION store.fn_check_quantity(p_product bigint, p_quantity numeric) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
BEGIN
  IF p_quantity IS NULL OR p_quantity <= 0 OR p_quantity <> round(p_quantity, 3) THEN
    RAISE EXCEPTION 'Quantity must be positive with at most 3 decimal places' USING ERRCODE = '23514';
  END IF;
  IF EXISTS (SELECT 1 FROM store.products WHERE id = p_product AND unit = 'pcs')
     AND p_quantity <> trunc(p_quantity) THEN
    RAISE EXCEPTION 'Piece goods require an integer quantity' USING ERRCODE = '23514';
  END IF;
END $$;

CREATE FUNCTION store.fn_sale_total(p_sale bigint) RETURNS numeric
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
  SELECT coalesce(sum(round(quantity * unit_price, 2) - discount_amount), 0)
  FROM store.sale_items WHERE sale_id = p_sale
$$;

CREATE FUNCTION store.fn_supply_total(p_supply bigint) RETURNS numeric
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
  SELECT coalesce(sum(round(quantity * unit_cost, 2)), 0)
  FROM store.supply_items WHERE supply_id = p_supply
$$;

CREATE FUNCTION store.fn_audit() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
BEGIN
  NEW.updated_by := store.fn_actor_id();
  NEW.updated_at := clock_timestamp();
  RETURN NEW;
END $$;

CREATE FUNCTION store.fn_no_delete() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, store, pg_temp AS $$
BEGIN
  RAISE EXCEPTION 'Physical deletion from % is forbidden; use soft delete or cancel a draft', TG_TABLE_NAME
    USING ERRCODE = '23514';
END $$;

CREATE FUNCTION store.fn_fixed_key() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, store, pg_temp AS $$
DECLARE k text;
BEGIN
  FOREACH k IN ARRAY TG_ARGV LOOP
    IF (to_jsonb(NEW)->k) IS DISTINCT FROM (to_jsonb(OLD)->k) THEN
      RAISE EXCEPTION 'Key %.% is immutable', TG_TABLE_NAME, k USING ERRCODE = '23514';
    END IF;
  END LOOP;
  RETURN NEW;
END $$;

CREATE FUNCTION store.fn_validate_catalog() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
BEGIN
  IF TG_TABLE_NAME = 'products' THEN
    IF NEW.deleted_at IS NULL THEN PERFORM store.fn_require_active('categories', NEW.category_id); END IF;
    IF TG_OP = 'UPDATE' AND NEW.unit IS DISTINCT FROM OLD.unit
       AND EXISTS (SELECT 1 FROM store.inventory WHERE product_id = OLD.id) THEN
      RAISE EXCEPTION 'Cannot change unit after inventory was created' USING ERRCODE = '23514';
    END IF;
  ELSIF TG_TABLE_NAME = 'employees' THEN
    IF NEW.deleted_at IS NULL THEN PERFORM store.fn_require_active('departments', NEW.department_id); END IF;
  ELSIF TG_TABLE_NAME = 'users' THEN
    IF NEW.deleted_at IS NULL AND NEW.employee_id IS NOT NULL THEN
      PERFORM store.fn_require_active('employees', NEW.employee_id);
    END IF;
    IF TG_OP = 'UPDATE' AND OLD.id = 1 AND
       (NEW.deleted_at IS NOT NULL OR NEW.username <> OLD.username OR NEW.role_id <> OLD.role_id
        OR NEW.employee_id IS DISTINCT FROM OLD.employee_id) THEN
      RAISE EXCEPTION 'Bootstrap identity is immutable' USING ERRCODE = '23514';
    END IF;
  ELSIF TG_TABLE_NAME = 'product_suppliers' THEN
    IF NEW.deleted_at IS NULL THEN
      PERFORM store.fn_require_active('products', NEW.product_id);
      PERFORM store.fn_require_active('suppliers', NEW.supplier_id);
    END IF;
  ELSIF TG_TABLE_NAME = 'inventory' THEN
    PERFORM store.fn_require_active('products', NEW.product_id);
    PERFORM store.fn_require_active('departments', NEW.department_id);
    IF EXISTS (SELECT 1 FROM store.products WHERE id = NEW.product_id AND unit = 'pcs')
       AND (NEW.quantity <> trunc(NEW.quantity) OR NEW.minimum_quantity <> trunc(NEW.minimum_quantity)) THEN
      RAISE EXCEPTION 'Piece inventory requires integer quantities' USING ERRCODE = '23514';
    END IF;
  END IF;
  RETURN NEW;
END $$;

-- Header lock serializes edits of child rows with posting.
CREATE FUNCTION store.fn_guard_detail() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
DECLARE v_doc bigint; v_status text; v_supplier bigint;
BEGIN
  IF TG_TABLE_NAME = 'supply_items' THEN
    v_doc := CASE WHEN TG_OP = 'DELETE' THEN OLD.supply_id ELSE NEW.supply_id END;
    IF TG_OP = 'UPDATE' AND NEW.supply_id <> OLD.supply_id THEN
      RAISE EXCEPTION 'Cannot move a document line' USING ERRCODE = '23514';
    END IF;
    SELECT status, supplier_id INTO v_status, v_supplier FROM store.supplies WHERE id = v_doc FOR UPDATE;
  ELSE
    v_doc := CASE WHEN TG_OP = 'DELETE' THEN OLD.sale_id ELSE NEW.sale_id END;
    IF TG_OP = 'UPDATE' AND NEW.sale_id <> OLD.sale_id THEN
      RAISE EXCEPTION 'Cannot move a document line' USING ERRCODE = '23514';
    END IF;
    SELECT status INTO v_status FROM store.sales WHERE id = v_doc FOR UPDATE;
  END IF;
  IF v_status IS DISTINCT FROM 'draft' THEN
    RAISE EXCEPTION 'Only an existing draft may be edited' USING ERRCODE = '23514';
  END IF;
  IF TG_OP <> 'DELETE' THEN
    IF TG_TABLE_NAME = 'payments' THEN
      IF TG_OP = 'INSERT' THEN NEW.created_by := store.fn_actor_id();
      ELSE NEW.created_by := OLD.created_by; END IF;
    ELSE
      PERFORM store.fn_require_active('products', NEW.product_id);
      PERFORM store.fn_require_active('departments', NEW.department_id);
      PERFORM store.fn_check_quantity(NEW.product_id, NEW.quantity);
      IF TG_TABLE_NAME = 'supply_items' THEN
        PERFORM store.fn_require_active('suppliers', v_supplier);
        PERFORM 1 FROM store.product_suppliers WHERE product_id = NEW.product_id
          AND supplier_id = v_supplier AND deleted_at IS NULL FOR SHARE;
        IF NOT FOUND THEN RAISE EXCEPTION 'Supplier does not offer this product' USING ERRCODE = '23514'; END IF;
      END IF;
    END IF;
  END IF;
  IF TG_TABLE_NAME = 'supply_items' THEN
    UPDATE store.supplies SET updated_by = store.fn_actor_id() WHERE id = v_doc;
  ELSE
    UPDATE store.sales SET updated_by = store.fn_actor_id() WHERE id = v_doc;
  END IF;
  IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
  RETURN NEW;
END $$;

CREATE FUNCTION store.fn_guard_document() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
DECLARE r record; v_total numeric; v_paid numeric;
BEGIN
  IF TG_OP = 'INSERT' THEN
    IF NEW.status <> 'draft' THEN RAISE EXCEPTION 'New document must be a draft' USING ERRCODE = '23514'; END IF;
    IF TG_TABLE_NAME = 'sales' THEN NEW.cashier_id := store.fn_actor_id();
    ELSE NEW.created_by := store.fn_actor_id(); END IF;
  ELSE
    IF OLD.status <> 'draft' THEN RAISE EXCEPTION 'Finalized document is immutable' USING ERRCODE = '23514'; END IF;
    IF TG_TABLE_NAME = 'sales' THEN
      IF NEW.cashier_id <> OLD.cashier_id OR NEW.receipt_number <> OLD.receipt_number
         OR NEW.customer_id IS DISTINCT FROM OLD.customer_id THEN
        RAISE EXCEPTION 'Sale identity is immutable' USING ERRCODE = '23514';
      END IF;
    ELSIF NEW.supplier_id <> OLD.supplier_id OR NEW.created_by <> OLD.created_by
          OR NEW.document_number <> OLD.document_number THEN
      RAISE EXCEPTION 'Supply identity is immutable' USING ERRCODE = '23514';
    END IF;
  END IF;
  IF TG_TABLE_NAME = 'sales' THEN
    IF TG_OP = 'INSERT' OR NEW.status = 'posted' THEN
      IF NEW.customer_id IS NOT NULL THEN PERFORM store.fn_require_active('customers', NEW.customer_id); END IF;
      PERFORM store.fn_require_active('users', NEW.cashier_id);
    END IF;
    IF NEW.status = 'cancelled' AND EXISTS (SELECT 1 FROM store.payments WHERE sale_id = NEW.id) THEN
      RAISE EXCEPTION 'Cannot cancel a draft with payments' USING ERRCODE = '23514';
    END IF;
    IF NEW.status = 'posted' THEN
      IF NOT EXISTS (SELECT 1 FROM store.sale_items WHERE sale_id = NEW.id) THEN
        RAISE EXCEPTION 'Cannot post an empty sale' USING ERRCODE = '23514';
      END IF;
      v_total := store.fn_sale_total(NEW.id);
      SELECT coalesce(sum(amount), 0) INTO v_paid FROM store.payments WHERE sale_id = NEW.id;
      IF v_total <> v_paid THEN RAISE EXCEPTION 'Payment mismatch: total %, paid %', v_total, v_paid USING ERRCODE = '23514'; END IF;
      FOR r IN SELECT * FROM store.sale_items WHERE sale_id = NEW.id ORDER BY department_id, product_id LOOP
        PERFORM store.fn_require_active('products', r.product_id);
        PERFORM store.fn_require_active('departments', r.department_id);
        PERFORM store.fn_check_quantity(r.product_id, r.quantity);
        -- Conditional UPDATE rechecks stock after a competing transaction commits.
        UPDATE store.inventory SET quantity = quantity - r.quantity
        WHERE department_id = r.department_id AND product_id = r.product_id AND quantity >= r.quantity;
        IF NOT FOUND THEN RAISE EXCEPTION 'Insufficient stock for product % in department %', r.product_id, r.department_id USING ERRCODE = '23514'; END IF;
      END LOOP;
      NEW.sold_at := clock_timestamp();
    END IF;
  ELSE
    IF TG_OP = 'INSERT' OR NEW.status = 'posted' THEN PERFORM store.fn_require_active('suppliers', NEW.supplier_id); END IF;
    IF NEW.status = 'posted' THEN
      IF NOT EXISTS (SELECT 1 FROM store.supply_items WHERE supply_id = NEW.id) THEN
        RAISE EXCEPTION 'Cannot post an empty supply' USING ERRCODE = '23514';
      END IF;
      FOR r IN SELECT * FROM store.supply_items WHERE supply_id = NEW.id ORDER BY department_id, product_id LOOP
        PERFORM store.fn_require_active('products', r.product_id);
        PERFORM store.fn_require_active('departments', r.department_id);
        PERFORM store.fn_check_quantity(r.product_id, r.quantity);
        PERFORM 1 FROM store.product_suppliers WHERE product_id = r.product_id
          AND supplier_id = NEW.supplier_id AND deleted_at IS NULL FOR SHARE;
        IF NOT FOUND THEN RAISE EXCEPTION 'Inactive supplier product' USING ERRCODE = '23514'; END IF;
        UPDATE store.inventory SET quantity = quantity + r.quantity
        WHERE department_id = r.department_id AND product_id = r.product_id;
        IF NOT FOUND THEN RAISE EXCEPTION 'Missing inventory row' USING ERRCODE = '23514'; END IF;
      END LOOP;
      NEW.received_at := clock_timestamp();
    END IF;
  END IF;
  RETURN NEW;
END $$;


