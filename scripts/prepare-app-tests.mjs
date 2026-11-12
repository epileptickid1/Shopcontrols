import {spawnSync} from 'node:child_process';
import {checked,connectionEnv} from '../tests/db.mjs';
const db=process.argv[2]||'store_webtests';
if(!/^store_webtests(?:_[a-z0-9]+)?$/.test(db))throw new Error('Use a dedicated store_webtests or store_webtests_suffix database');
connectionEnv.PGDATABASE='postgres';
const exists=await checked("SELECT EXISTS(SELECT 1 FROM pg_database WHERE datname='"+db+"')");
if(exists==='f'){
 const install=spawnSync(process.execPath,['scripts/install-db.mjs',db],{stdio:'inherit',windowsHide:true});
 if(install.status!==0)process.exit(1);
}
const setup=spawnSync(process.execPath,['scripts/setup-app.mjs',db],{stdio:'inherit',windowsHide:true});
if(setup.status!==0)process.exit(1);

