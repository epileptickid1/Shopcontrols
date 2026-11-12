import {checked,connectionEnv} from '../tests/db.mjs';
import {spawnSync} from 'node:child_process';
const database=process.argv[2]||'store_lab';
if(!/^[a-z][a-z0-9_]{0,62}$/.test(database))throw new Error('Use a lowercase ASCII database name');
connectionEnv.PGDATABASE='postgres';
const exists=await checked("SELECT EXISTS(SELECT 1 FROM pg_database WHERE datname='"+database+"')");
if(exists==='t')throw new Error('Database already exists: '+database+'. Choose a new name; nothing was deleted.');
await checked('CREATE DATABASE "'+database+'"');
for(const file of ['sql/install.sql','sql/08_demo_data.sql']){
 const r=spawnSync(process.execPath,['scripts/run-sql.mjs',file],{stdio:'inherit',env:{...process.env,PGDATABASE:database},windowsHide:true});
 if(r.status!==0)throw new Error('Installation failed: '+file+'. Database preserved for diagnosis.');
}
console.log('Ready: '+database);

