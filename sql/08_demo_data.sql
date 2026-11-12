\set ON_ERROR_STOP on
BEGIN;
-- Demo-only database identities. No login or passwords until an application stack is chosen.
DO $$
BEGIN
 IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='store_manager_demo') THEN CREATE ROLE store_manager_demo NOLOGIN IN ROLE store_app; END IF;
 IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='store_cashier_demo') THEN CREATE ROLE store_cashier_demo NOLOGIN IN ROLE store_app; END IF;
END $$;
INSERT INTO store.departments(name,floor) VALUES ('Продуктовий',1),('Побутова техніка',2),('Канцтовари',1);
INSERT INTO store.employees(department_id,full_name,job_title,hired_on) VALUES
 (1,'Олена Коваль','Менеджер','2024-01-10'),(1,'Андрій Мельник','Касир','2025-03-01');
INSERT INTO store.users(employee_id,role_id,username,password_hash) VALUES
 (1,(SELECT id FROM store.roles WHERE name='manager'),'store_manager_demo','!disabled:database-authentication-only!'),
 (2,(SELECT id FROM store.roles WHERE name='cashier'),'store_cashier_demo','!disabled:database-authentication-only!');
INSERT INTO store.categories(name) VALUES ('Молочні продукти'),('Фрукти'),('Електроприлади'),('Зошити');
INSERT INTO store.suppliers(name,registration_code,phone,email) VALUES
 ('ТОВ Демопостачання','DEMO-0001','+380000000001','supplier@example.test'),
 ('ТОВ Технодемо','DEMO-0002','+380000000002','tech@example.test');
INSERT INTO store.customers(full_name,phone,email,loyalty_card) VALUES
 ('Марія Тестова','+380000000003','customer@example.test','DEMO-CARD-001');
SET LOCAL SESSION AUTHORIZATION store_manager_demo;
CALL store.sp_save_product(1,'MILK-001','Молоко 1 л','pcs',50,NULL,'DEMO-BAR-001');
CALL store.sp_save_product(2,'APPLE-001','Яблука','kg',40,NULL,'DEMO-BAR-002');
CALL store.sp_save_product(3,'KETTLE-001','Електрочайник','pcs',1200,NULL,'DEMO-BAR-003');
CALL store.sp_save_product(4,'BOOK-001','Зошит','pcs',20,NULL,'DEMO-BAR-004');
CALL store.sp_set_supplier_product(1,1,35);
CALL store.sp_set_supplier_product(2,1,25);
CALL store.sp_set_supplier_product(3,2,900);
CALL store.sp_set_supplier_product(4,1,12);
CALL store.sp_create_supply(1,'DEMO-SUP-001');
CALL store.sp_add_supply_item(1,1,1,100);
CALL store.sp_add_supply_item(1,1,2,50.5);
CALL store.sp_add_supply_item(1,3,4,100);
CALL store.sp_post_supply(1);
CALL store.sp_create_supply(2,'DEMO-SUP-002');
CALL store.sp_add_supply_item(2,2,3,10);
CALL store.sp_post_supply(2);
CALL store.sp_set_minimum_stock(1,1,20);
CALL store.sp_set_minimum_stock(1,2,10);
RESET SESSION AUTHORIZATION;
SET LOCAL SESSION AUTHORIZATION store_cashier_demo;
CALL store.sp_create_sale('DEMO-SALE-001',1);
CALL store.sp_add_sale_item(1,1,1,2);
CALL store.sp_add_sale_item(1,1,2,1.25);
CALL store.sp_add_payment(1,'cash',100);
CALL store.sp_add_payment(1,'card',50,'DEMO-PAY-001');
CALL store.sp_post_sale(1);
RESET SESSION AUTHORIZATION;
\ir 11_demo_manager.sql
COMMIT;

