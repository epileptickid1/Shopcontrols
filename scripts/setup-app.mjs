import {randomBytes} from 'node:crypto';
import {existsSync,readFileSync,writeFileSync} from 'node:fs';
import path from 'node:path';
import {checked,connectionEnv,root} from '../tests/db.mjs';

const database=process.argv[2]||'store_lab';
if(!/^[a-z][a-z0-9_]{0,62}$/.test(database))throw new Error('Invalid database name');
connectionEnv.PGDATABASE=database;
// Repeatable upgrade of an existing database; edited names are never reseeded.
await checked('\\i sql/migrations/001_manager_profiles.sql');
const managers=[['store_manager_demo','manager-password.txt'],['store_manager_demo_2','manager-2-password.txt']];
const connections=[];
for(const [username,passwordFile] of managers){
 const role=JSON.parse(await checked("SELECT json_build_object('safe',EXISTS(SELECT 1 FROM pg_roles a WHERE a.rolname='"+username+"' "+
  "AND pg_has_role(a.oid,'store_app','MEMBER') AND NOT pg_has_role(a.oid,'store_owner','MEMBER') "+
  "AND NOT EXISTS(SELECT 1 FROM pg_roles r WHERE pg_has_role(a.oid,r.oid,'MEMBER') "+
  "AND (r.rolsuper OR r.rolcreaterole OR r.rolcreatedb OR r.rolbypassrls OR r.rolreplication))),"+
  "'active',EXISTS(SELECT 1 FROM store.v_manager_profiles WHERE username='"+username+"'));"));
 if(!role.safe||!role.active)throw new Error('Expected active limited manager role was not found: '+username);
 const pwFile=path.join(root,'.runtime',passwordFile);
 const password=existsSync(pwFile)?readFileSync(pwFile,'utf8').trim():randomBytes(32).toString('base64url');
 if(!/^[A-Za-z0-9_-]{40,}$/.test(password))throw new Error('Unexpected local manager password format');
 await checked("ALTER ROLE "+username+" LOGIN PASSWORD '"+password+"'; GRANT CONNECT ON DATABASE "+database+" TO "+username+";");
 writeFileSync(pwFile,password+'\n');
 connections.push("Host=127.0.0.1;Port=55432;Database="+database+";Username="+username+";Password="+password+
  ";Timeout=5;Command Timeout=30;Maximum Pool Size=10;Include Error Detail=false");
}
const [store,secondManager]=connections;
if(database==='store_lab'){
 writeFileSync(path.join(root,'.runtime','app-connection.json'),JSON.stringify({ConnectionStrings:{Store:store},ManagerConnections:[secondManager]},null,2)+'\n');
}else{
 writeFileSync(path.join(root,'.runtime','app-test-connection.txt'),store+'\n');
 writeFileSync(path.join(root,'.runtime','app-test-manager-2-connection.txt'),secondManager+'\n');
}
console.log('Two limited demo managers configured for '+database+'. Credentials saved locally; not printed.');

