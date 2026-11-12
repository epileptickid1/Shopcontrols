import {readFileSync} from 'node:fs';
import path from 'node:path';
import {checked,root} from '../tests/db.mjs';
function expand(file){
 const full=path.resolve(root,file);
 return readFileSync(full,'utf8').replace(/^\s*\\ir\s+(.+)$/gm,(_,relative)=>expand(path.resolve(path.dirname(full),relative.trim())));
}
const file=process.argv[2];
if(!file)throw new Error('Usage: node scripts/run-sql.mjs sql/install.sql');
console.log(await checked(expand(file),{timeout:240000,applicationName:'store-sql-script'}));

