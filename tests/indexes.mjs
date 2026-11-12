import {checked,report} from './db.mjs';
const query="\nBEGIN;\n-- Actual tables and constraints. All benchmark data/index changes roll back.\nINSERT INTO store.customers(full_name,loyalty_card)\nSELECT 'Benchmark customer '||g,'BENCH-'||g FROM generate_series(1,1000) g;\nINSERT INTO store.sales(receipt_number,customer_id,cashier_id,sold_at)\nSELECT 'BENCH-'||g,c.id,1,timestamptz '2025-01-01 00:00:00+00'+g*interval '1 minute'\nFROM generate_series(1,60000) g\nJOIN store.customers c ON c.loyalty_card='BENCH-'||((g%1000)+1);\nANALYZE store.sales;\nSELECT id AS bench_customer FROM store.customers WHERE loyalty_card='BENCH-1' \\gset\nDROP INDEX store.ix_sales_sold_at;\nDROP INDEX store.ix_sales_customer_hash;\n\\echo BEFORE_BTREE\nEXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)\nSELECT * FROM store.sales WHERE sold_at>='2025-01-10 00:00:00+00' AND sold_at<'2025-01-10 00:10:00+00';\n\\echo BEFORE_HASH\nEXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)\nSELECT * FROM store.sales WHERE customer_id=:bench_customer;\nCREATE INDEX ix_sales_sold_at ON store.sales USING btree(sold_at);\nCREATE INDEX ix_sales_customer_hash ON store.sales USING hash(customer_id);\nANALYZE store.sales;\n\\echo AFTER_BTREE\nEXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)\nSELECT * FROM store.sales WHERE sold_at>='2025-01-10 00:00:00+00' AND sold_at<'2025-01-10 00:10:00+00';\n\\echo AFTER_HASH\nEXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)\nSELECT * FROM store.sales WHERE customer_id=:bench_customer;\n\\echo END_BENCHMARK\nROLLBACK;\n";

const output=await checked(query,{timeout:240000,applicationName:'store-index-benchmark'});
const markers=['BEFORE_BTREE','BEFORE_HASH','AFTER_BTREE','AFTER_HASH','END_BENCHMARK'];
const plans={};
for(let i=0;i<4;i++){
 const value=output.slice(output.indexOf(markers[i])+markers[i].length,output.indexOf(markers[i+1])).trim();
 plans[markers[i]]=JSON.parse(value)[0];
}
const containsIndex=(p,name)=>p['Index Name']===name||(p.Plans||[]).some(c=>containsIndex(c,name));
if(!containsIndex(plans.AFTER_BTREE.Plan,'ix_sales_sold_at'))throw new Error('B-tree not selected by optimizer');
if(!containsIndex(plans.AFTER_HASH.Plan,'ix_sales_customer_hash'))throw new Error('Hash index not selected by optimizer');
report('index-benchmark.json',{executedAt:new Date().toISOString(),rowsAdded:60000,defaultPlanner:true,rolledBack:true,plans});
for(const [name,p]of Object.entries(plans))console.log(name+': '+p.Plan['Node Type']+', '+p['Execution Time']+' ms');

