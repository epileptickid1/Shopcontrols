import {checked,report} from '../tests/db.mjs';
const query = "SELECT json_build_object("+
 "'version',version(),"+
 "'tables',(SELECT count(*) FROM pg_tables WHERE schemaname='store'),"+
 "'views',(SELECT count(*) FROM pg_views WHERE schemaname='store'),"+
 "'procedures',(SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='store' AND p.prokind='p'),"+
 "'functions',(SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='store' AND p.prokind='f'),"+
 "'triggers',(SELECT count(*) FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='store' AND NOT t.tgisinternal),"+
 "'foreignKeys',(SELECT count(*) FROM pg_constraint co JOIN pg_namespace n ON n.oid=co.connamespace WHERE n.nspname='store' AND co.contype='f'),"+
 "'demoSales',(SELECT json_agg(s) FROM store.v_sales_summary s),"+
 "'demoInventory',(SELECT json_agg(i) FROM store.v_inventory i));";
const data=JSON.parse(await checked(query));
report('database-inventory.json',data);
console.log(JSON.stringify({version:data.version,tables:data.tables,views:data.views,procedures:data.procedures,functions:data.functions,triggers:data.triggers,foreignKeys:data.foreignKeys},null,2));

