CREATE VIEW store.v_active_products AS
 SELECT p.id,p.category_id,c.name AS category_name,p.sku,p.barcode,p.name,p.unit,p.sale_price,p.updated_at,p.updated_by
 FROM store.products p JOIN store.categories c ON c.id=p.category_id
 WHERE p.deleted_at IS NULL AND c.deleted_at IS NULL;
CREATE VIEW store.v_product_archive AS
 SELECT id,sku,name,deleted_at,updated_at,updated_by FROM store.products WHERE deleted_at IS NOT NULL;
CREATE VIEW store.v_inventory AS
 SELECT i.department_id,d.name AS department_name,i.product_id,p.name AS product_name,p.sku,p.unit,
 i.quantity,i.minimum_quantity,i.quantity < i.minimum_quantity AS needs_restock,i.updated_at,i.updated_by
 FROM store.inventory i JOIN store.departments d ON d.id=i.department_id
 JOIN store.v_active_products p ON p.id=i.product_id WHERE d.deleted_at IS NULL;
CREATE VIEW store.v_sales_summary AS
 SELECT s.id,s.receipt_number,s.customer_id,c.full_name AS customer_name,s.cashier_id,u.username AS cashier,
 s.sold_at,s.status,store.fn_sale_total(s.id) AS total,
 coalesce(pay.paid,0) AS paid,store.fn_sale_total(s.id)-coalesce(pay.paid,0) AS balance,s.updated_at,s.updated_by
 FROM store.sales s LEFT JOIN store.customers c ON c.id=s.customer_id JOIN store.users u ON u.id=s.cashier_id
 LEFT JOIN (SELECT sale_id,sum(amount) AS paid FROM store.payments GROUP BY sale_id) pay ON pay.sale_id=s.id;
CREATE VIEW store.v_sale_items AS
 SELECT i.*,p.name AS product_name,p.sku,p.unit,d.name AS department_name,
 round(i.quantity*i.unit_price,2)-i.discount_amount AS line_total
 FROM store.sale_items i JOIN store.products p ON p.id=i.product_id JOIN store.departments d ON d.id=i.department_id;
CREATE VIEW store.v_payments AS SELECT id,sale_id,method,amount,paid_at,transaction_reference,created_by FROM store.payments;
CREATE VIEW store.v_supplies_summary AS
 SELECT s.*,sp.name AS supplier_name,store.fn_supply_total(s.id) AS total
 FROM store.supplies s JOIN store.suppliers sp ON sp.id=s.supplier_id;
CREATE VIEW store.v_supply_items AS
 SELECT i.*,p.name AS product_name,d.name AS department_name,round(i.quantity*i.unit_cost,2) AS line_total
 FROM store.supply_items i JOIN store.products p ON p.id=i.product_id JOIN store.departments d ON d.id=i.department_id;
CREATE VIEW store.v_active_customers AS SELECT id,full_name,phone,email,loyalty_card FROM store.customers WHERE deleted_at IS NULL;
CREATE VIEW store.v_active_departments AS SELECT id,name,floor FROM store.departments WHERE deleted_at IS NULL;
CREATE VIEW store.v_active_categories AS SELECT id,name,description FROM store.categories WHERE deleted_at IS NULL;
CREATE VIEW store.v_active_suppliers AS SELECT id,name,registration_code,phone,email,address FROM store.suppliers WHERE deleted_at IS NULL;
CREATE VIEW store.v_supplier_products AS
 SELECT ps.product_id,ps.supplier_id,p.name AS product_name,s.name AS supplier_name,ps.supplier_sku,ps.purchase_price
 FROM store.product_suppliers ps JOIN store.v_active_products p ON p.id=ps.product_id
 JOIN store.v_active_suppliers s ON s.id=ps.supplier_id WHERE ps.deleted_at IS NULL;
CREATE VIEW store.v_current_user AS
 SELECT u.id,u.username,e.full_name,r.name AS role_name
 FROM store.users u JOIN store.roles r ON r.id=u.role_id LEFT JOIN store.employees e ON e.id=u.employee_id
 LEFT JOIN store.departments d ON d.id=e.department_id
 WHERE u.username=session_user AND u.deleted_at IS NULL
 AND (u.employee_id IS NULL OR (e.deleted_at IS NULL AND d.deleted_at IS NULL));
CREATE VIEW store.v_daily_revenue AS
 SELECT (sold_at AT TIME ZONE 'Europe/Kyiv')::date AS sale_date,count(*) AS receipt_count,sum(total) AS revenue
 FROM store.v_sales_summary WHERE status='posted' GROUP BY (sold_at AT TIME ZONE 'Europe/Kyiv')::date;

