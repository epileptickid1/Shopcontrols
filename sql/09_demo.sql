\set ON_ERROR_STOP on
-- Run as the administrator for demonstration. Everything is rolled back.
BEGIN;
SET LOCAL SESSION AUTHORIZATION store_manager_demo;
SELECT * FROM store.v_current_user;
CALL store.sp_soft_delete_product(4);
SELECT * FROM store.v_product_archive;
CALL store.sp_restore_product(4);
CALL store.sp_create_supply(1,'PREVIEW-SUPPLY',NULL) \gset
\set supply_id :p_id
CALL store.sp_add_supply_item(:supply_id,1,1,10);
CALL store.sp_post_supply(:supply_id);
SELECT * FROM store.v_supplies_summary WHERE id=:supply_id;
RESET SESSION AUTHORIZATION;
SET LOCAL SESSION AUTHORIZATION store_cashier_demo;
CALL store.sp_create_sale('PREVIEW-SALE',1,NULL) \gset
\set sale_id :p_id
CALL store.sp_add_sale_item(:sale_id,1,1,1);
CALL store.sp_add_sale_item(:sale_id,1,2,0.750,5);
CALL store.sp_add_payment(:sale_id,'cash',25);
CALL store.sp_add_payment(:sale_id,'card',50,'PREVIEW-PAYMENT');
CALL store.sp_post_sale(:sale_id);
SELECT * FROM store.v_sales_summary WHERE id=:sale_id;
SELECT * FROM store.v_sale_items WHERE sale_id=:sale_id;
SELECT * FROM store.v_payments WHERE sale_id=:sale_id;
SELECT * FROM store.v_inventory ORDER BY department_id,product_id;
ROLLBACK;

