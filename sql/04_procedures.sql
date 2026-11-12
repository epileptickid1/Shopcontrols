-- Public write API. Procedures participate in the caller's transaction; no COMMIT.
CREATE PROCEDURE store.sp_save_product(
 p_category_id bigint, p_sku text, p_name text, p_unit text, p_price numeric,
 INOUT p_id bigint DEFAULT NULL, p_barcode text DEFAULT NULL)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
BEGIN
 PERFORM store.fn_actor_id(ARRAY['manager']);
 PERFORM store.fn_require_active('categories', p_category_id);
 IF p_price IS NULL OR p_price < 0 OR p_price::text IN ('NaN','Infinity','-Infinity') OR p_price <> round(p_price,2) THEN
  RAISE EXCEPTION 'Invalid price' USING ERRCODE='23514';
 END IF;
 IF p_id IS NULL THEN
  INSERT INTO store.products(category_id,sku,name,unit,sale_price,barcode)
  VALUES(p_category_id,p_sku,p_name,p_unit,p_price,p_barcode) RETURNING id INTO p_id;
 ELSE
  UPDATE store.products SET category_id=p_category_id,sku=p_sku,name=p_name,unit=p_unit,sale_price=p_price,barcode=p_barcode
  WHERE id=p_id AND deleted_at IS NULL;
  IF NOT FOUND THEN RAISE EXCEPTION 'Active product not found' USING ERRCODE='23514'; END IF;
 END IF;
END $$;

CREATE PROCEDURE store.sp_soft_delete_product(p_id bigint)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
BEGIN
 PERFORM store.fn_actor_id(ARRAY['manager']);
 UPDATE store.products SET deleted_at=clock_timestamp() WHERE id=p_id AND deleted_at IS NULL;
 IF NOT FOUND THEN RAISE EXCEPTION 'Active product not found' USING ERRCODE='23514'; END IF;
END $$;

CREATE PROCEDURE store.sp_restore_product(p_id bigint)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
BEGIN
 PERFORM store.fn_actor_id(ARRAY['manager']);
 UPDATE store.products SET deleted_at=NULL WHERE id=p_id AND deleted_at IS NOT NULL;
 IF NOT FOUND THEN RAISE EXCEPTION 'Deleted product not found' USING ERRCODE='23514'; END IF;
END $$;

CREATE PROCEDURE store.sp_set_supplier_product(p_product bigint,p_supplier bigint,p_price numeric,p_sku text DEFAULT NULL)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
BEGIN
 PERFORM store.fn_actor_id(ARRAY['manager']);
 IF p_price IS NULL OR p_price < 0 OR p_price <> round(p_price,2) THEN
  RAISE EXCEPTION 'Invalid purchase price' USING ERRCODE='23514';
 END IF;
 INSERT INTO store.product_suppliers(product_id,supplier_id,purchase_price,supplier_sku)
 VALUES(p_product,p_supplier,p_price,p_sku)
 ON CONFLICT(product_id,supplier_id) DO UPDATE SET purchase_price=excluded.purchase_price,
  supplier_sku=excluded.supplier_sku,deleted_at=NULL;
END $$;

CREATE FUNCTION store.fn_lock_sale(p_id bigint) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
DECLARE v_status text; v_cashier bigint; v_actor bigint;
BEGIN
 v_actor := store.fn_actor_id(ARRAY['cashier','manager']);
 SELECT status,cashier_id INTO v_status,v_cashier FROM store.sales WHERE id=p_id FOR UPDATE;
 IF v_status IS DISTINCT FROM 'draft' THEN RAISE EXCEPTION 'Sale is missing or is not a draft' USING ERRCODE='23514'; END IF;
 IF v_cashier <> v_actor THEN PERFORM store.fn_actor_id(ARRAY['manager']); END IF;
END $$;

CREATE FUNCTION store.fn_lock_supply(p_id bigint) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
DECLARE v_status text;
BEGIN
 PERFORM store.fn_actor_id(ARRAY['manager']);
 SELECT status INTO v_status FROM store.supplies WHERE id=p_id FOR UPDATE;
 IF v_status IS DISTINCT FROM 'draft' THEN RAISE EXCEPTION 'Supply is missing or is not a draft' USING ERRCODE='23514'; END IF;
END $$;

CREATE PROCEDURE store.sp_create_sale(p_receipt text,p_customer bigint DEFAULT NULL,INOUT p_id bigint DEFAULT NULL)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
BEGIN
 PERFORM store.fn_actor_id(ARRAY['cashier','manager']);
 INSERT INTO store.sales(receipt_number,customer_id,cashier_id)
 VALUES(p_receipt,p_customer,store.fn_actor_id()) RETURNING id INTO p_id;
END $$;

CREATE PROCEDURE store.sp_add_sale_item(p_sale bigint,p_department bigint,p_product bigint,p_quantity numeric,p_discount numeric DEFAULT 0)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
DECLARE v_price numeric;
BEGIN
 PERFORM store.fn_lock_sale(p_sale);
 PERFORM store.fn_require_active('products',p_product);
 PERFORM store.fn_require_active('departments',p_department);
 PERFORM store.fn_check_quantity(p_product,p_quantity);
 IF p_discount IS NULL OR p_discount < 0 OR p_discount <> round(p_discount,2) THEN
  RAISE EXCEPTION 'Invalid discount' USING ERRCODE='23514';
 END IF;
 SELECT sale_price INTO v_price FROM store.products WHERE id=p_product;
 INSERT INTO store.sale_items(sale_id,department_id,product_id,quantity,unit_price,discount_amount)
 VALUES(p_sale,p_department,p_product,p_quantity,v_price,p_discount);
END $$;

CREATE PROCEDURE store.sp_remove_sale_item(p_sale bigint,p_item bigint)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
BEGIN
 PERFORM store.fn_lock_sale(p_sale);
 DELETE FROM store.sale_items WHERE id=p_item AND sale_id=p_sale;
 IF NOT FOUND THEN RAISE EXCEPTION 'Sale line not found' USING ERRCODE='23514'; END IF;
END $$;

CREATE PROCEDURE store.sp_add_payment(p_sale bigint,p_method text,p_amount numeric,p_reference text DEFAULT NULL)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
BEGIN
 PERFORM store.fn_lock_sale(p_sale);
 IF p_amount IS NULL OR p_amount <= 0 OR p_amount <> round(p_amount,2) THEN
  RAISE EXCEPTION 'Invalid payment amount' USING ERRCODE='23514';
 END IF;
 INSERT INTO store.payments(sale_id,method,amount,transaction_reference,created_by)
 VALUES(p_sale,p_method,p_amount,p_reference,store.fn_actor_id());
END $$;

CREATE PROCEDURE store.sp_post_sale(p_sale bigint)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
BEGIN
 PERFORM store.fn_lock_sale(p_sale);
 UPDATE store.sales SET status='posted' WHERE id=p_sale;
END $$;

CREATE PROCEDURE store.sp_cancel_sale(p_sale bigint)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
BEGIN
 PERFORM store.fn_lock_sale(p_sale);
 UPDATE store.sales SET status='cancelled' WHERE id=p_sale;
END $$;

CREATE PROCEDURE store.sp_create_supply(p_supplier bigint,p_document text,INOUT p_id bigint DEFAULT NULL)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
BEGIN
 PERFORM store.fn_actor_id(ARRAY['manager']);
 INSERT INTO store.supplies(supplier_id,document_number,created_by)
 VALUES(p_supplier,p_document,store.fn_actor_id()) RETURNING id INTO p_id;
END $$;

CREATE PROCEDURE store.sp_add_supply_item(p_supply bigint,p_department bigint,p_product bigint,p_quantity numeric,p_cost numeric DEFAULT NULL)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
DECLARE v_supplier bigint; v_cost numeric;
BEGIN
 PERFORM store.fn_lock_supply(p_supply);
 PERFORM store.fn_require_active('products',p_product);
 PERFORM store.fn_require_active('departments',p_department);
 PERFORM store.fn_check_quantity(p_product,p_quantity);
 SELECT supplier_id INTO v_supplier FROM store.supplies WHERE id=p_supply;
 PERFORM store.fn_require_active('suppliers',v_supplier);
 SELECT purchase_price INTO v_cost FROM store.product_suppliers
 WHERE product_id=p_product AND supplier_id=v_supplier AND deleted_at IS NULL FOR SHARE;
 IF NOT FOUND THEN RAISE EXCEPTION 'Supplier does not offer this product' USING ERRCODE='23514'; END IF;
 v_cost := coalesce(p_cost,v_cost);
 IF v_cost < 0 OR v_cost <> round(v_cost,2) THEN RAISE EXCEPTION 'Invalid purchase price' USING ERRCODE='23514'; END IF;
 INSERT INTO store.inventory(department_id,product_id) VALUES(p_department,p_product) ON CONFLICT DO NOTHING;
 INSERT INTO store.supply_items(supply_id,department_id,product_id,quantity,unit_cost)
 VALUES(p_supply,p_department,p_product,p_quantity,v_cost);
END $$;

CREATE PROCEDURE store.sp_remove_supply_item(p_supply bigint,p_item bigint)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
BEGIN
 PERFORM store.fn_lock_supply(p_supply);
 DELETE FROM store.supply_items WHERE id=p_item AND supply_id=p_supply;
 IF NOT FOUND THEN RAISE EXCEPTION 'Supply line not found' USING ERRCODE='23514'; END IF;
END $$;

CREATE PROCEDURE store.sp_post_supply(p_supply bigint)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
BEGIN
 PERFORM store.fn_lock_supply(p_supply);
 UPDATE store.supplies SET status='posted' WHERE id=p_supply;
END $$;

CREATE PROCEDURE store.sp_cancel_supply(p_supply bigint)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
BEGIN
 PERFORM store.fn_lock_supply(p_supply);
 UPDATE store.supplies SET status='cancelled' WHERE id=p_supply;
END $$;

CREATE PROCEDURE store.sp_set_minimum_stock(p_department bigint,p_product bigint,p_minimum numeric)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, store, pg_temp AS $$
BEGIN
 PERFORM store.fn_actor_id(ARRAY['manager']);
 IF p_minimum IS NULL OR p_minimum < 0 OR p_minimum <> round(p_minimum,3) THEN
  RAISE EXCEPTION 'Invalid minimum stock' USING ERRCODE='23514';
 END IF;
 UPDATE store.inventory SET minimum_quantity=p_minimum WHERE department_id=p_department AND product_id=p_product;
 IF NOT FOUND THEN RAISE EXCEPTION 'Inventory not found' USING ERRCODE='23514'; END IF;
END $$;

