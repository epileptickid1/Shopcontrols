import {checked,sql,report} from './db.mjs';
const suffix=Date.now().toString();
const setup=await checked("BEGIN; SET LOCAL SESSION AUTHORIZATION store_manager_demo;"+
 "DO $$ DECLARE p bigint; s bigint; a bigint; b bigint; BEGIN "+
 "CALL store.sp_save_product(4,'RACE-"+suffix+"','Concurrency test','pcs',10,p);"+
 "CALL store.sp_set_supplier_product(p,1,5);"+
 "CALL store.sp_create_supply(1,'RACE-SUP-"+suffix+"',s); CALL store.sp_add_supply_item(s,3,p,1); CALL store.sp_post_supply(s);"+
 "CALL store.sp_create_sale('RACE-A-"+suffix+"',NULL,a); CALL store.sp_add_sale_item(a,3,p,1); CALL store.sp_add_payment(a,'cash',10);"+
 "CALL store.sp_create_sale('RACE-B-"+suffix+"',NULL,b); CALL store.sp_add_sale_item(b,3,p,1); CALL store.sp_add_payment(b,'cash',10);"+
 "END $$; COMMIT; SELECT id FROM store.products WHERE sku='RACE-"+suffix+"';"+
 "SELECT id FROM store.sales WHERE receipt_number IN ('RACE-A-"+suffix+"','RACE-B-"+suffix+"') ORDER BY id;");
const [pid,aid,bid]=setup.trim().split(/\s+/).map(Number);
if(![pid,aid,bid].every(Number.isSafeInteger))throw new Error('Unexpected fixture IDs: '+setup);
const barrier=7000000000000+Number(suffix.slice(-10));
const gateName='store-race-gate-'+suffix;
const workerName='store-race-first-'+suffix;
const secondName='store-race-second-'+suffix;
async function waitFor(query,timeout=15000){
 const until=Date.now()+timeout;
 while(Date.now()<until){
  if((await checked(query))==='t')return;
  await new Promise(r=>setTimeout(r,100));
 }
 throw new Error('Concurrency barrier timed out: '+query);
}
const gate=sql("SELECT pg_advisory_lock("+barrier+"); SELECT pg_sleep(30);",{applicationName:gateName,timeout:45000});
let first,second;
try{
 await waitFor("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE application_name='"+gateName+"' AND wait_event='PgSleep')");
 first=sql("BEGIN; SET LOCAL SESSION AUTHORIZATION store_manager_demo; CALL store.sp_post_sale("+aid+");"+
  "SELECT pg_advisory_xact_lock("+barrier+"); COMMIT;",{applicationName:workerName,timeout:45000});
 await waitFor("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE application_name='"+workerName+"' AND wait_event_type='Lock' AND wait_event='advisory')");
 second=sql("BEGIN; SET LOCAL SESSION AUTHORIZATION store_manager_demo; CALL store.sp_post_sale("+bid+"); COMMIT;",
  {applicationName:secondName,timeout:45000});
 await waitFor("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE application_name='"+secondName+"' AND wait_event_type='Lock')");
 await checked("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name='"+gateName+"';");
 const [g,a,b]=await Promise.all([gate,first,second]);
 if(a.code!==0||b.code===0||!b.stderr.includes('Insufficient stock'))throw new Error(JSON.stringify({a,b}));
 const state=JSON.parse(await checked("SELECT json_build_object('stock',(SELECT quantity FROM store.inventory WHERE department_id=3 AND product_id="+pid+"),"+
  "'posted',(SELECT count(*) FROM store.sales WHERE id IN ("+aid+","+bid+") AND status='posted'),"+
  "'draft',(SELECT count(*) FROM store.sales WHERE id IN ("+aid+","+bid+") AND status='draft'));"));
 if(state.stock!==0||state.posted!==1||state.draft!==1)throw new Error('Invalid final state '+JSON.stringify(state));
 report('concurrency-tests.json',{executedAt:new Date().toISOString(),passed:true,scenario:'Two sessions buy the last unit',blockingObserved:true,state,loserError:b.stderr});
 console.log('PASS concurrency: second sale waited, one posted, one rejected, stock = 0');
}finally{
 await checked("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name IN ('"+gateName+"','"+workerName+"','"+secondName+"');");
 await Promise.allSettled([gate,first,second].filter(Boolean));
}

