import {readFileSync,writeFileSync} from 'node:fs';
import path from 'node:path';
const root=process.cwd();
function expand(p){
 return readFileSync(p,'utf8').replace(/^\s*\\ir\s+(.+)$/gm,(_,r)=>'\n-- Source: '+r+'\n'+expand(path.resolve(path.dirname(p),r.trim())))
  .replace(/^\\set[^\r\n]*$/gm,'');
}
let install=expand(path.join(root,'sql','install.sql')).replace(/COMMIT;\s*$/,'RESET ROLE;\n');
let demo=expand(path.join(root,'sql','08_demo_data.sql')).replace(/^BEGIN;\s*$/m,'');
writeFileSync(path.join(root,'store_complete.sql'),
 '-- PostgreSQL 18. Fresh dedicated database; run as administrator. Entire script is atomic.\n'+
 '-- Generated from store.sql and sql/*.sql. Rebuild: node scripts/build-sql.mjs\n'+install+'\n'+demo);
console.log('Built store_complete.sql');

