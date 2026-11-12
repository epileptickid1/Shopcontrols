import { spawn } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const portableRoot=readFileSync(path.join(root,'.runtime','portable-root.txt'),'utf8').trim();
export const psqlPath = process.env.PSQL_PATH || path.join(portableRoot,'pgsql','bin','psql.exe');
export const connectionEnv = {
 ...process.env, PGHOST:process.env.PGHOST||'127.0.0.1', PGPORT:process.env.PGPORT||'55432',
 PGUSER:process.env.PGUSER||'postgres', PGDATABASE:process.env.PGDATABASE||'store_lab',
 PGPASSWORD:process.env.PGPASSWORD||readFileSync(path.join(root,'.runtime','postgres-password.txt'),'utf8').trim(),
 PGCLIENTENCODING:'UTF8'
};
export function sql(text, {applicationName='store-tests', timeout=120000}={}) {
 return new Promise((resolve,reject)=>{
  const proc=spawn(psqlPath,['-X','-q','-A','-t','-v','ON_ERROR_STOP=1'],{
   cwd:root,env:{...connectionEnv,PGAPPNAME:applicationName},windowsHide:true
  });
  let stdout='',stderr='';
  const timer=setTimeout(()=>{proc.kill();reject(new Error('psql timed out: '+applicationName));},timeout);
  proc.stdout.on('data',b=>stdout+=b.toString('utf8'));
  proc.stderr.on('data',b=>stderr+=b.toString('utf8'));
  proc.on('error',e=>{clearTimeout(timer);reject(e)});
  proc.on('close',code=>{clearTimeout(timer);resolve({code,stdout,stderr})});
  proc.stdin.end(text);
 });
}
export async function checked(text, options) {
 const r=await sql(text,options);
 if(r.code!==0) throw new Error(r.stderr||r.stdout);
 return r.stdout.trim();
}
export const assertSql=(condition,message)=>"IF NOT ("+condition+") IS TRUE THEN RAISE EXCEPTION '"+message.replaceAll("'","''")+"'; END IF;";
export const expectSqlError=(statement,code='23514')=>
 "BEGIN "+statement+"; RAISE EXCEPTION 'Expected SQLSTATE "+code+"'; EXCEPTION WHEN SQLSTATE '"+code+"' THEN NULL; END;";
export function report(name,result){
 const directory=path.join(root,'reports');
 mkdirSync(directory,{recursive:true});
 writeFileSync(path.join(directory,name),JSON.stringify(result,null,2)+'\n');
}


