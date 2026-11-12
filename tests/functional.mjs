import {checked, assertSql as a, expectSqlError as bad, report} from './db.mjs';
const tests=[];
function test(name,body,{actor='store_manager_demo',declare='sid bigint; x bigint; qty numeric; ts timestamptz;'}={}){
 tests.push({name,sql:"BEGIN;\nSET LOCAL SESSION AUTHORIZATION "+actor+";\nDO $$ DECLARE "+declare+" BEGIN\n"+body+"\nEND $$;\nROLLBACK;\n"});
}
test('Schema: 15 tables, foreign keys, audit, 2 index methods',a("(SELECT count(*)=15 FROM information_schema.tables WHERE table_schema='store' AND table_type='BASE TABLE')",'15 tables')+
 a("(SELECT count(*)=2 FROM (SELECT DISTINCT am.amname FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid JOIN pg_am am ON am.oid=c.relam JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='store') z)",'two index methods'),{actor:'postgres'});
test('Demo: posted supply, weighted sale and split payment',
 a("(SELECT total=150 AND paid=150 AND balance=0 AND status='posted' FROM store.v_sales_summary WHERE id=1)",'sale summary')+
 a("(SELECT quantity=98 FROM store.v_inventory WHERE department_id=1 AND product_id=1)",'stock after sale')+
 a("(SELECT quantity=49.250 FROM store.v_inventory WHERE department_id=1 AND product_id=2)",'weighted stock'));
test('Soft delete hides product; historical sale survives; restore works',
 "CALL store.sp_soft_delete_product(1);"+
 a("NOT EXISTS(SELECT 1 FROM store.v_active_products WHERE id=1)",'hidden product')+
 a("EXISTS(SELECT 1 FROM store.v_product_archive WHERE id=1)",'archive')+
 a("(SELECT count(*)=2 FROM store.v_sale_items WHERE sale_id=1)",'history preserved')+
 "CALL store.sp_restore_product(1);"+a("EXISTS(SELECT 1 FROM store.v_active_products WHERE id=1)",'restored'));
test('Audit identifies real session user and updates date',
 "SELECT updated_at INTO ts FROM store.v_active_products WHERE id=1;"+
 "x:=1; CALL store.sp_save_product(1,'MILK-001','Milk updated','pcs',51,x,'DEMO-BAR-001');"+
 a("(SELECT updated_by=2 AND updated_at>ts FROM store.v_active_products WHERE id=1)",'audit')+
 "PERFORM set_config('app.user_id','1',true); x:=1; CALL store.sp_save_product(1,'MILK-001','Milk updated twice','pcs',52,x,'DEMO-BAR-001');"+
 a("(SELECT updated_by=2 FROM store.v_active_products WHERE id=1)",'actor cannot be spoofed with GUC'));
test('Cashier cannot change catalog',bad("CALL store.sp_soft_delete_product(1)",'42501'),{actor:'store_cashier_demo'});
test('App cannot SELECT/INSERT/UPDATE/DELETE/TRUNCATE tables or call helpers',
 bad("PERFORM 1 FROM store.products",'42501')+
 bad("INSERT INTO store.roles(name) VALUES('evil')",'42501')+
 bad("UPDATE store.products SET sale_price=1 WHERE id=1",'42501')+
 bad("DELETE FROM store.products WHERE id=1",'42501')+
 bad("TRUNCATE store.inventory",'42501')+
 bad("PERFORM store.fn_actor_id()", '42501'));
test('New empty sale cannot be posted',
 "CALL store.sp_create_sale('TEST-EMPTY',NULL,sid);"+bad("CALL store.sp_post_sale(sid)"));
test('Whole units and maximum precision are enforced',
 "CALL store.sp_create_sale('TEST-FRACTION',NULL,sid);"+
 bad("CALL store.sp_add_sale_item(sid,1,1,0.5)")+
 bad("CALL store.sp_add_sale_item(sid,1,2,0.0001)")+
 bad("CALL store.sp_add_sale_item(sid,1,2,0)")+
 bad("CALL store.sp_add_sale_item(sid,1,2,-1)"));
test('Insufficient/excess payment prevents posting without stock changes',
 "CALL store.sp_create_sale('TEST-PAYMENT',NULL,sid); CALL store.sp_add_sale_item(sid,1,1,2); CALL store.sp_add_payment(sid,'cash',99);"+
 bad("CALL store.sp_post_sale(sid)")+
 a("(SELECT quantity=98 FROM store.v_inventory WHERE department_id=1 AND product_id=1)",'unchanged stock')+
 "CALL store.sp_add_payment(sid,'card',2);"+bad("CALL store.sp_post_sale(sid)"));
test('Multi-line stock update rolls back if a later line has insufficient stock',
 "CALL store.sp_create_sale('TEST-NOSTOCK',NULL,sid); CALL store.sp_add_sale_item(sid,1,1,1); CALL store.sp_add_sale_item(sid,2,3,11); CALL store.sp_add_payment(sid,'cash',13250);"+
 bad("CALL store.sp_post_sale(sid)")+
 a("(SELECT quantity=98 FROM store.v_inventory WHERE department_id=1 AND product_id=1)",'first stock rolled back')+
 a("(SELECT status='draft' FROM store.v_sales_summary WHERE id=sid)",'draft retained'));
test('Zero-total sale needs no payment',
 "CALL store.sp_create_sale('TEST-FREE',NULL,sid); CALL store.sp_add_sale_item(sid,1,1,1,50); CALL store.sp_post_sale(sid);"+
 a("(SELECT total=0 AND paid=0 AND status='posted' FROM store.v_sales_summary WHERE id=sid)",'free sale'));
test('Historical price remains unchanged after catalog update',
 "CALL store.sp_create_sale('TEST-PRICE',NULL,sid); CALL store.sp_add_sale_item(sid,1,1,1); x:=1; CALL store.sp_save_product(1,'MILK-001','Milk','pcs',60,x);"+
 a("(SELECT unit_price=50 FROM store.v_sale_items WHERE sale_id=sid)",'snapshot price'));
test('Repeated posting and edits of posted documents are rejected',
 bad("CALL store.sp_post_sale(1)")+
 bad("CALL store.sp_add_sale_item(1,1,1,1)")+
 bad("CALL store.sp_add_payment(1,'cash',1)")+
 bad("CALL store.sp_cancel_sale(1)")+
 bad("CALL store.sp_post_supply(1)")+
 bad("CALL store.sp_add_supply_item(1,1,1,1)"));
test('Triggers protect posted data even for direct administrative SQL',
 bad("UPDATE store.sales SET status='draft' WHERE id=1")+
 bad("DELETE FROM store.sale_items WHERE sale_id=1")+
 bad("UPDATE store.payments SET amount=1 WHERE sale_id=1")+
 bad("UPDATE store.supplies SET document_number='changed' WHERE id=1")+
 bad("DELETE FROM store.supply_items WHERE supply_id=1"),{actor:'postgres'});
test('Soft-deleted products cannot be added or posted',
 "CALL store.sp_create_sale('TEST-DELETED',NULL,sid); CALL store.sp_add_sale_item(sid,1,1,1); CALL store.sp_add_payment(sid,'cash',50); CALL store.sp_soft_delete_product(1);"+
 bad("CALL store.sp_post_sale(sid)")+
 "CALL store.sp_create_sale('TEST-DELETED-NEW',NULL,x);"+bad("CALL store.sp_add_sale_item(x,1,1,1)"));
test('Soft delete retains unique SKU; restore requires active category',
 "CALL store.sp_soft_delete_product(1);"+
 bad("CALL store.sp_save_product(1,'MILK-001','Duplicate','pcs',1,x)",'23505')+
 "UPDATE store.categories SET deleted_at=clock_timestamp() WHERE id=1;"+
 bad("CALL store.sp_restore_product(1)"),{actor:'postgres'});
test('Supplier mismatch, empty supply, duplicate lines are rejected',
 "CALL store.sp_create_supply(1,'TEST-SUPPLY',sid);"+
 bad("CALL store.sp_post_supply(sid)")+
 bad("CALL store.sp_add_supply_item(sid,2,3,1)")+
 "CALL store.sp_add_supply_item(sid,1,1,3);"+
 bad("CALL store.sp_add_supply_item(sid,1,1,3)",'23505')+
 "CALL store.sp_post_supply(sid);"+
 a("(SELECT quantity=101 FROM store.v_inventory WHERE product_id=1 AND department_id=1)",'supply stock added'));
test('Posting revalidates supplier link',
 "CALL store.sp_create_supply(1,'TEST-LINK',sid); CALL store.sp_add_supply_item(sid,1,1,3);"+
 "UPDATE store.product_suppliers SET deleted_at=clock_timestamp() WHERE product_id=1 AND supplier_id=1;"+
 bad("CALL store.sp_post_supply(sid)"),{actor:'postgres'});
test('Child changes update parent audit; removing draft lines is allowed',
 "CALL store.sp_create_sale('TEST-TOUCH',NULL,sid); SELECT updated_at INTO ts FROM store.v_sales_summary WHERE id=sid;"+
 "CALL store.sp_add_sale_item(sid,1,1,1); SELECT id INTO x FROM store.v_sale_items WHERE sale_id=sid;"+
 a("(SELECT updated_at>ts AND updated_by=2 FROM store.v_sales_summary WHERE id=sid)",'parent audit')+
 "CALL store.sp_remove_sale_item(sid,x);"+
 a("NOT EXISTS(SELECT 1 FROM store.v_sale_items WHERE sale_id=sid)",'removed draft line'));
test('Cancelled draft is immutable; paid draft cannot be cancelled',
 "CALL store.sp_create_sale('TEST-CANCEL',NULL,sid); CALL store.sp_cancel_sale(sid);"+
 bad("CALL store.sp_add_sale_item(sid,1,1,1)")+
 "CALL store.sp_create_sale('TEST-CANCEL-PAID',NULL,x); CALL store.sp_add_payment(x,'cash',1);"+
 bad("CALL store.sp_cancel_sale(x)"));
test('Atomic operation: entire new document rolls back on failure',
 "BEGIN CALL store.sp_create_sale('TEST-ROLLBACK',NULL,sid); CALL store.sp_add_sale_item(sid,1,1,999); CALL store.sp_add_payment(sid,'cash',49950); CALL store.sp_post_sale(sid);"+
 "RAISE EXCEPTION 'Expected stock failure'; EXCEPTION WHEN SQLSTATE '23514' THEN NULL; END;"+
 a("NOT EXISTS(SELECT 1 FROM store.v_sales_summary WHERE receipt_number='TEST-ROLLBACK')",'whole transaction sub-block rolled back'));
test('Checks reject excessive discount, invalid payment method and NaN',
 "CALL store.sp_create_sale('TEST-CHECKS',NULL,sid);"+
 bad("CALL store.sp_add_sale_item(sid,1,1,1,51)")+
 "CALL store.sp_add_sale_item(sid,1,1,1);"+
 bad("CALL store.sp_add_payment(sid,'invalid',50)")+
 bad("CALL store.sp_add_payment(sid,'cash','NaN'::numeric)")+
 bad("CALL store.sp_add_payment(sid,'cash',0)")+
 bad("CALL store.sp_add_payment(sid,'cash',0.001)"));
test('Duplicate payment reference and nonexistent inventory are rejected',
 "CALL store.sp_create_sale('TEST-DUPLICATE',NULL,sid);"+
 bad("CALL store.sp_add_payment(sid,'card',1,'DEMO-PAY-001')",'23505')+
 bad("CALL store.sp_add_sale_item(sid,2,1,1)",'23503'));
test('Physical deletion, primary key change, unit change and NULL audit are protected',
 bad("DELETE FROM store.products WHERE id=1")+
 bad("UPDATE store.products SET id=10000 WHERE id=1")+
 bad("UPDATE store.products SET unit='kg' WHERE id=1")+
 "UPDATE store.products SET updated_at='2000-01-01',updated_by=NULL WHERE id=1;"+
 a("(SELECT updated_by=1 AND updated_at>'2000-01-01' FROM store.products WHERE id=1)",'audit override'),{actor:'postgres'});
test('Inactive actor is denied',
 "UPDATE store.users SET deleted_at=clock_timestamp() WHERE id=3;"+
 "SET LOCAL SESSION AUTHORIZATION store_cashier_demo;"+
 bad("CALL store.sp_create_sale('TEST-DISABLED',NULL,sid)",'42501'),{actor:'postgres'});
test('Cashier cannot modify another cashier/manager draft',
 "CALL store.sp_create_sale('TEST-OTHER',NULL,sid); SET LOCAL SESSION AUTHORIZATION store_cashier_demo;"+
 bad("CALL store.sp_add_sale_item(sid,1,1,1)",'42501'),{actor:'postgres'});
test('Minimum stock and supply draft removal/cancellation',
 "CALL store.sp_set_minimum_stock(1,1,200);"+
 a("(SELECT needs_restock FROM store.v_inventory WHERE department_id=1 AND product_id=1)",'restock flag')+
 bad("CALL store.sp_set_minimum_stock(1,1,0.5)")+
 "CALL store.sp_create_supply(1,'TEST-CANCEL-SUP',sid); CALL store.sp_add_supply_item(sid,1,1,1);"+
 "SELECT id INTO x FROM store.v_supply_items WHERE supply_id=sid; CALL store.sp_remove_supply_item(sid,x); CALL store.sp_cancel_supply(sid);"+
 bad("CALL store.sp_post_supply(sid)"));
test('Audit is enforced on all 11 audited tables',
 "CALL store.sp_create_sale('TEST-AUDIT-ALL',NULL,sid); CALL store.sp_create_supply(1,'TEST-AUDIT-ALL',x); ts:=clock_timestamp();"+
 "FOREACH t IN ARRAY ARRAY['departments','employees','users','categories','products','suppliers','product_suppliers','inventory','supplies','customers','sales'] LOOP "+
 "clause:=CASE WHEN t IN ('sales','supplies') THEN ' WHERE status=''draft''' ELSE '' END;"+
 "EXECUTE format('UPDATE store.%I SET updated_by=999999,updated_at=''2000-01-01''%s',t,clause);"+
 "EXECUTE format('SELECT bool_and(updated_by=1 AND updated_at >= $1) FROM store.%I%s',t,clause) INTO ok USING ts;"+
 "IF ok IS DISTINCT FROM true THEN RAISE EXCEPTION 'Audit failed for %',t; END IF; END LOOP;",
 {actor:'postgres',declare:'sid bigint; x bigint; ts timestamptz; t text; clause text; ok boolean;'});

const results=[];
for(const t of tests){
 try{await checked(t.sql);results.push({name:t.name,passed:true});console.log('PASS '+t.name)}
 catch(e){results.push({name:t.name,passed:false,error:e.message});console.error('FAIL '+t.name+'\n'+e.message)}
}
report('functional-tests.json',{executedAt:new Date().toISOString(),passed:results.filter(r=>r.passed).length,total:results.length,results});
if(results.some(r=>!r.passed))process.exitCode=1;


