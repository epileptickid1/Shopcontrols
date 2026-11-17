import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { parseArgs } from 'node:util';
import { benchmark, benchmarkMarkdown } from './benchmark.mjs';
import { demoReceipts, validateReceipt } from './data.mjs';
import { runGraph } from './graph.mjs';
import { fromMongo, MongoArchive } from './mongo.mjs';
import { PostgresArchive, sql } from './postgres.mjs';

const defaults = {
  postgres: 'postgresql://lab:shopcontrols-lab-only@127.0.0.1:55433/shopcontrols_lab',
  mongo: 'mongodb://lab:shopcontrols-lab-only@127.0.0.1:57017/?authSource=admin',
  neo4j: 'bolt://127.0.0.1:57687'
};
const env = process.env;
const resultsDir = new URL('../results/', import.meta.url);
const options = {
  'sale-id': { type: 'string' }, number: { type: 'string' },
  sizes: { type: 'string', default: '10000,100000' }, samples: { type: 'string', default: '120' },
  dataset: { type: 'string', default: 'demo' }, 'receipt-id': { type: 'string' },
  product: { type: 'string' }, help: { type: 'boolean', short: 'h' }
};

function usage() {
  console.log(`npm run lab -- demo
npm run lab -- import [--sale-id 1]
npm run lab -- receipt --number DEMO-SALE-001
npm run lab -- graph [--dataset shopcontrols --receipt-id 1 --product 1]
npm run lab -- benchmark [--sizes 10000,100000 --samples 120]`);
}

async function report(name, value) {
  await mkdir(resultsDir, { recursive: true });
  await writeFile(new URL(`${name}.json`, resultsDir), JSON.stringify(value, null, 2) + '\n');
  console.log(`Результат: nosql-lab/results/${name}.json`);
}

async function sourceSettings() {
  if (env.SHOPCONTROLS_SOURCE_URL) return { connectionString: env.SHOPCONTROLS_SOURCE_URL };
  const path = new URL('../../.runtime/manager-password.txt', import.meta.url);
  const password = await readFile(path, 'utf8').catch((error) => {
    if (error.code !== 'ENOENT') throw error;
    throw new Error('Налаштуй PostgreSQL Shopcontrols через scripts/setup-app.ps1 або задай SHOPCONTROLS_SOURCE_URL.');
  });
  return { host: '127.0.0.1', port: 55432, database: 'store_lab', user: 'store_manager_demo',
    password: password.trim() };
}

async function storeBatch(pg, mongo, rows) {
  rows.forEach(validateReceipt);
  const insertedSql = await pg.insert(rows);
  // Read back the canonical SQL snapshots. This repairs a failed MongoDB write on retry
  // without replacing names if Shopcontrols data changed after a prior SQL commit.
  const canonical = await pg.byIds(rows.map((r) => r.id));
  assert.equal(canonical.length, rows.length);
  const insertedMongo = await mongo.insert(canonical);
  // Check every newly imported or previously archived receipt against the canonical copy.
  const mongoCopies = new Map();
  for await (const row of mongo.collection.find({ _id: { $in: canonical.map((r) => r.id) } })) {
    mongoCopies.set(row._id, fromMongo(row));
  }
  for (const row of canonical) assert.deepEqual(mongoCopies.get(row.id), row, `Archive mismatch for ${row.number}`);
  return { insertedSql, insertedMongo, verified: canonical.length };
}

async function main() {
  const { values, positionals } = parseArgs({ options, allowPositionals: true });
  if (values.help || !positionals.length) { usage(); return; }
  const [command] = positionals;
  if (positionals.length !== 1 || !['demo', 'import', 'receipt', 'graph', 'benchmark'].includes(command)) {
    throw new Error('Невідома команда. Виконай npm run lab -- --help.');
  }
  const count = (value, min, max) => {
    if (!/^\d+$/.test(value) || Number(value) < min || Number(value) > max) {
      throw new Error(`Очікується ціле число від ${min} до ${max}: ${value}`);
    }
    return Number(value);
  };
  const sizes = command === 'benchmark' ? [...new Set(values.sizes.split(',').map((x) => count(x, 10, 1_000_000)))] : [];
  const samples = command === 'benchmark' ? count(values.samples, 12, 5000) : 0;
  const saleId = values['sale-id'] ?? null;
  if (saleId !== null && (!/^[1-9]\d{0,18}$/.test(saleId) || BigInt(saleId) > 9223372036854775807n)) {
    throw new Error('--sale-id має бути додатним PostgreSQL bigint.');
  }
  if (command === 'receipt' && !values.number?.trim()) throw new Error('Вкажи --number.');
  if (command === 'graph' && !['demo', 'shopcontrols'].includes(values.dataset)) {
    throw new Error('--dataset має бути demo або shopcontrols.');
  }

  const { MongoClient } = await import('mongodb');
  const mongoClient = new MongoClient(env.LAB_MONGO_URL ?? defaults.mongo,
    { maxPoolSize: 1, serverSelectionTimeoutMS: 5000 });
  let client;
  let source;
  let driver;
  try {
    await mongoClient.connect();
    const mongoDb = mongoClient.db(env.LAB_MONGO_DATABASE ?? 'shopcontrols_lab');
    const mongo = new MongoArchive(mongoDb);
    if (command === 'graph') {
      const { default: neo4j } = await import('neo4j-driver');
      driver = neo4j.driver(env.LAB_NEO4J_URL ?? defaults.neo4j,
        neo4j.auth.basic(env.LAB_NEO4J_USER ?? 'neo4j', env.LAB_NEO4J_PASSWORD ?? 'shopcontrols-lab-only'),
        { disableLosslessIntegers: true });
      const result = await runGraph(driver, mongo.collection, {
        dataset: values.dataset, productId: values.product, receiptId: values['receipt-id']
      });
      console.table(result.items);
      console.table(result.count);
      console.table(result.together);
      await report(`graph-${values.dataset}`, result);
      return;
    }

    const { default: { Client } } = await import('pg');
    client = new Client({ connectionString: env.LAB_POSTGRES_URL ?? defaults.postgres, connectionTimeoutMillis: 5000 });
    await client.connect();
    if (command === 'benchmark') {
      const name = samples === 120 && sizes.join(',') === '10000,100000' ? 'benchmark' : 'benchmark-custom';
      const result = await benchmark(client, mongoDb, sizes, samples);
      await report(name, result);
      await writeFile(new URL(`${name}.md`, resultsDir), benchmarkMarkdown(result));
      return;
    }
    const pg = new PostgresArchive(client);
    if (command === 'receipt') {
      const receipt = await pg.full(values.number);
      assert.deepEqual(await mongo.full(values.number), receipt);
      if (!receipt) throw new Error(`Чек ${values.number} відсутній в архіві.`);
      console.log(JSON.stringify(receipt, null, 2));
      return;
    }
    await pg.init();
    await mongo.init();
    if (command === 'demo') {
      const input = demoReceipts();
      const first = await storeBatch(pg, mongo, input);
      const second = await storeBatch(pg, mongo, input);
      assert.equal(second.insertedSql, 0);
      assert.equal(second.insertedMongo, 0);
      const receipt = await pg.full(input[0].number);
      assert.deepEqual(receipt, input[0]);
      const recent = await pg.byProduct('1');
      assert.deepEqual(await mongo.byProduct('1'), recent);
      const from = '2025-01-01T00:00:00.000Z';
      const to = '2025-02-01T00:00:00.000Z';
      const paymentTotals = await pg.paymentsByMethod(from, to);
      assert.deepEqual(await mongo.paymentsByMethod(from, to), paymentTotals);
      console.log(JSON.stringify(receipt, null, 2));
      await report('demo', { executedAt: new Date().toISOString(), resultsMatch: true, first, repeat: second,
        receipt, recent, paymentTotals });
      return;
    }

    source = new Client({ ...await sourceSettings(), connectionTimeoutMillis: 5000 });
    await source.connect();
    const totals = { insertedSql: 0, insertedMongo: 0, verified: 0 };
    let after = '0';
    for (;;) {
      const { rows } = await source.query(sql('export-shopcontrols'), [after, saleId, 500]);
      if (!rows.length) break;
      const receipts = rows.map((r) => r.receipt);
      const result = await storeBatch(pg, mongo, receipts);
      for (const key of Object.keys(totals)) totals[key] += result[key];
      after = receipts.at(-1).id;
      if (saleId !== null) break;
    }
    if (!totals.verified) throw new Error('Проведених чеків для імпорту не знайдено.');
    console.log(totals);
    await report('shopcontrols-import', { executedAt: new Date().toISOString(), saleId,
      resultsMatch: true, ...totals });
  } finally {
    await Promise.allSettled([client?.end(), source?.end(), mongoClient.close(), driver?.close()]);
  }
}

main().catch((error) => {
  console.error(`Помилка: ${error.message}`);
  process.exitCode = 1;
});
