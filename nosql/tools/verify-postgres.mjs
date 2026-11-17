import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { batches, benchmarkNumber, demoReceipts, generate, validateReceipt } from '../src/data.mjs';
import { archiveSql, PostgresArchive, sql } from '../src/postgres.mjs';

const samples = demoReceipts();
const schema = 'receipt_bench_' + randomBytes(6).toString('hex');
const pg = new PostgresArchive(null, schema);
const from = '2025-01-01T00:00:00.000Z';
const to = '2025-02-01T00:00:00.000Z';
const includeStore = process.argv.includes('--with-store-demo');
const verifyLoad = process.argv.includes('--load-check');
const quoteJson = (value) => {
  const tag = '$receipt_' + randomBytes(8).toString('hex') + '$';
  const json = JSON.stringify(value);
  assert.ok(!json.includes(tag));
  return tag + json + tag;
};
const modified = structuredClone(samples[0]);
modified.items[0].productName = 'Змінена після архівування назва';
const invalid = structuredClone(samples[0]);
invalid.id = 'rollback-test';
invalid.number = 'ROLLBACK-TEST';
invalid.items[0].quantityMilli = 0;
const workloadSize = 10_000;
const generated = verifyLoad ? [...generate(workloadSize)] : [];
const loadSql = verifyLoad ? `
${[...batches(generated, 500)].map((batch) => `EXECUTE lab_insert(${quoteJson(batch)});`).join('\n')}
ANALYZE ${schema}.receipts;
ANALYZE ${schema}.receipt_items;
ANALYZE ${schema}.receipt_payments;
SELECT json_build_object(
    'receipts', (SELECT count(*) FROM ${schema}.receipts),
    'items', (SELECT count(*) FROM ${schema}.receipt_items),
    'payments', (SELECT count(*) FROM ${schema}.receipt_payments));
EXECUTE lab_full('${benchmarkNumber(workloadSize)}');
EXECUTE lab_recent('123');
SELECT '@PLANS@';
EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) EXECUTE lab_full('${benchmarkNumber(workloadSize)}');
SELECT '@PLANS@';
EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) EXECUTE lab_recent('123');
SELECT '@PLANS@';
EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) EXECUTE lab_totals('${from}', '${to}');
` : '';

const input = `
BEGIN;
CREATE SCHEMA ${schema};
${archiveSql('schema', schema)}
PREPARE lab_insert(jsonb) AS ${pg.insertSql}
PREPARE lab_full(text) AS ${pg.fullSql}
PREPARE lab_recent(text) AS
  SELECT row_to_json(q) FROM (${pg.productSql.replace(/;$/, '')}) q;
PREPARE lab_totals(timestamptz, timestamptz) AS
  SELECT row_to_json(q) FROM (${pg.paymentsSql.replace(/;$/, '')}) q;
SELECT json_build_object('version', version());
EXECUTE lab_insert(${quoteJson(samples)});
EXECUTE lab_insert(${quoteJson([modified])});
DO $verify$
BEGIN
  EXECUTE $command$EXECUTE lab_insert(${quoteJson([invalid])})$command$;
  RAISE EXCEPTION 'Invalid row was accepted';
EXCEPTION WHEN check_violation THEN
  NULL;
END
$verify$;
SELECT count(*) FROM ${schema}.receipts WHERE sale_id = 'rollback-test';
EXECUTE lab_full('NO-SQL-DEMO-1');
EXECUTE lab_recent('1');
EXECUTE lab_totals('${from}', '${to}');
${includeStore ? `
SET LOCAL SESSION AUTHORIZATION store_manager_demo;
PREPARE lab_export(bigint, bigint, integer) AS ${sql('export-shopcontrols')}
EXECUTE lab_export(0, 1, 500);
RESET SESSION AUTHORIZATION;
` : ''}
${loadSql}
ROLLBACK;
`;
const psql = spawnSync(process.env.LAB_PSQL ?? 'psql', ['-X', '-w', '-qAt', '-v', 'ON_ERROR_STOP=1'], {
  input, encoding: 'utf8', env: { ...process.env, PGCLIENTENCODING: 'UTF8' }, windowsHide: true
});
if (psql.error) throw psql.error;
if (psql.status !== 0) throw new Error(psql.stderr || psql.stdout);
const [data, ...explainBlocks] = psql.stdout.trim().split(/^@PLANS@\r?$/m);
const output = data.trim().split(/\r?\n/).map(JSON.parse);
const [server, initialInsert, secondInsert, rollbackCount, receipt, ...rest] = output;
assert.equal(initialInsert, samples.length);
assert.equal(secondInsert, 0);
assert.equal(rollbackCount, 0);
assert.deepEqual(receipt, samples[0]);
const recent = rest.splice(0, 3);
assert.deepEqual(recent.map((r) => r.number), ['NO-SQL-DEMO-3', 'NO-SQL-DEMO-2', 'NO-SQL-DEMO-1']);
const paymentTotals = rest.splice(0, 3);
const expectedTotals = {};
for (const r of samples) {
  for (const p of r.payments) expectedTotals[p.method] = (expectedTotals[p.method] ?? 0) + p.amountMinor;
}
assert.deepEqual(Object.fromEntries(paymentTotals.map((p) => [p.method, p.amountMinor])), expectedTotals);
const realReceipt = includeStore ? rest.shift() : null;
if (includeStore) {
  validateReceipt(realReceipt);
  assert.equal(realReceipt.number, 'DEMO-SALE-001');
  assert.equal(realReceipt.totalMinor, 15000);
  assert.equal(realReceipt.items.length, 2);
  assert.equal(realReceipt.payments.length, 2);
}
let loadCheck = null;
if (verifyLoad) {
  const imported = rest.splice(0, workloadSize / 500);
  assert.equal(imported.length, workloadSize / 500);
  assert.ok(imported.every((n) => n === 500));
  const counts = rest.shift();
  const combined = [...samples, ...generated];
  assert.deepEqual(counts, {
    receipts: combined.length,
    items: combined.reduce((n, r) => n + r.items.length, 0),
    payments: combined.reduce((n, r) => n + r.payments.length, 0)
  });
  assert.deepEqual(rest.shift(), generated.at(-1));
  const expectedProduct = generated.filter((r) => r.items.some((i) => i.productId === '123'))
    .sort((a, b) => b.soldAt.localeCompare(a.soldAt) || a.number.localeCompare(b.number))
    .slice(0, 50).map((r) => r.number);
  assert.ok(expectedProduct.length > 0);
  assert.deepEqual(rest.splice(0).map((r) => r.number), expectedProduct);
  assert.equal(explainBlocks.length, 3);
  loadCheck = {
    generatedReceipts: workloadSize,
    countsIncludingFixture: counts,
    productQueryMatches: expectedProduct.length,
    sampleServerPlans: Object.fromEntries(['full', 'byProduct', 'paymentsByMethod'].map((op, i) => {
      const [plan] = JSON.parse(explainBlocks[i]);
      return [op, plan];
    }))
  };
}
assert.equal(rest.length, 0);
const evidence = { executedAt: new Date().toISOString(), postgres: server.version, passed: true,
  initialInsert, secondInsert, rollbackCount, receipt, recent, paymentTotals,
  shopcontrolsDemoExport: realReceipt, loadCheck };
const path = new URL('../results/', import.meta.url);
await mkdir(path, { recursive: true });
await writeFile(new URL('postgres-check.json', path), JSON.stringify(evidence, null, 2) + '\n');
console.log('PostgreSQL: schema, queries, repeat import and rollback verified. See results/postgres-check.json.');
