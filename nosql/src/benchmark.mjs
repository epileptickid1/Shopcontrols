import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { cpus, totalmem } from 'node:os';
import { performance } from 'node:perf_hooks';
import { batches, benchmarkNumber, generate, random, validateReceipt } from './data.mjs';
import { MongoArchive } from './mongo.mjs';
import { PostgresArchive } from './postgres.mjs';

const months = Array.from({ length: 12 }, (_, i) => [
  new Date(Date.UTC(2025, i, 1)).toISOString(), new Date(Date.UTC(2025, i + 1, 1)).toISOString()
]);

function workloads(count, samples) {
  const next = random(601032);
  return [
    { operation: 'full', label: 'Повний чек за номером', args: Array.from({ length: samples },
      () => [benchmarkNumber(1 + Math.floor(next() * count))]) },
    { operation: 'byProduct', label: 'Останні 50 чеків із товаром', args: Array.from({ length: samples },
      (_, i) => [String((i % 2 ? 901 : 1) + Math.floor(next() * 100))]) },
    { operation: 'paymentsByMethod', label: 'Суми оплат за способами за місяць', args: Array.from(
      { length: samples }, (_, i) => months[i % months.length]) }
  ];
}

export function latency(values) {
  const sorted = [...values].sort((a, b) => a - b);
  const n = sorted.length;
  const midpoint = Math.floor(n / 2);
  const median = n % 2 ? sorted[midpoint] : (sorted[midpoint - 1] + sorted[midpoint]) / 2;
  return { medianMs: Number(median.toFixed(4)), p95Ms: Number(sorted[Math.ceil(0.95 * n) - 1].toFixed(4)) };
}

export function benchmarkMarkdown(report) {
  const lines = [
    '# Вимірювання PostgreSQL і MongoDB',
    '',
    `Дата UTC: ${report.executedAt}`,
    '',
    `PostgreSQL: ${report.environment.postgres}  `,
    `MongoDB: ${report.environment.mongo}  `,
    `Node.js: ${report.environment.node}`,
    '',
    'Час у мілісекундах. На кожному виміряному наборі параметрів відповіді збіглися.',
    '',
    '| Чеків | Запит | Викликів на СУБД | SQL медіана / p95 | MongoDB медіана / p95 | Менша медіана |',
    '| ---: | --- | ---: | ---: | ---: | --- |'
  ];
  for (const dataset of report.datasets) {
    for (const q of dataset.queries) {
      const sql = q.postgres;
      const doc = q.mongo;
      let faster = 'Однакова після округлення';
      if (sql.medianMs < doc.medianMs) faster = 'PostgreSQL';
      if (doc.medianMs < sql.medianMs) faster = 'MongoDB';
      lines.push(`| ${dataset.size} | ${q.label} | ${q.samples} | ${sql.medianMs} / ${sql.p95Ms} | ${doc.medianMs} / ${doc.p95Ms} | ${faster} |`);
    }
  }
  lines.push('', 'Вимірювалося послідовне читання з прогрітими кешами. JSON-звіт містить плани та параметри запуску.', '');
  return lines.join('\n');
}

async function timed(query) {
  const start = performance.now();
  const value = await query();
  const elapsed = performance.now() - start;
  return { value, elapsed };
}

async function measure(pg, mongo, { operation, label, args }) {
  const engines = { postgres: pg, mongo };
  const times = { postgres: [], mongo: [] };
  const count = { min: Number.MAX_SAFE_INTEGER, max: 0 };
  for (const params of args.slice(0, 12)) {
    const sql = await pg[operation](...params);
    const doc = await mongo[operation](...params);
    assert.deepEqual(doc, sql, `Warm-up parity failed for ${operation}`);
  }
  for (const [i, params] of args.entries()) {
    const order = i % 2 === 0 ? ['postgres', 'mongo'] : ['mongo', 'postgres'];
    const results = {};
    for (const engine of order) {
      const { value, elapsed } = await timed(() => engines[engine][operation](...params));
      results[engine] = value;
      times[engine].push(elapsed);
    }
    assert.deepEqual(results.mongo, results.postgres, `Benchmark parity failed for ${operation} ${JSON.stringify(params)}`);
    const size = Array.isArray(results.postgres) ? results.postgres.length : Number(results.postgres !== null);
    if (operation === 'full') assert.equal(size, 1, 'Benchmark receipt missing.');
    count.min = Math.min(count.min, size);
    count.max = Math.max(count.max, size);
  }
  // Plans are diagnostics. They are collected after timing and never used as client-side latency.
  const plans = [];
  const explainArgs = operation === 'byProduct' ? args.slice(0, 2) : args.slice(0, 1);
  for (const params of explainArgs) {
    plans.push({ args: params, postgres: await pg.explain(operation, params), mongo: await mongo.explain(operation, params) });
  }
  return { operation, label, samples: args.length, warmup: Math.min(12, args.length), resultRows: count,
    postgres: latency(times.postgres), mongo: latency(times.mongo), plans };
}

export async function benchmark(client, mongoDb, sizes, samples) {
  const [{ rows }, mongoInfo] = await Promise.all([client.query('SELECT version()'), mongoDb.command({ buildInfo: 1 })]);
  const result = {
    executedAt: new Date().toISOString(),
    environment: { postgres: rows[0].version, mongo: mongoInfo.version, node: process.version,
      platform: process.platform, arch: process.arch, cpu: cpus()[0]?.model, hostMemoryBytes: totalmem() },
    seed: 20261118,
    measurement: 'One persistent connection per engine, sequential calls, alternating engine order, full result materialization, parity for every input. Import and explain are outside the timer.',
    datasets: []
  };
  for (const size of sizes) {
    const suffix = randomBytes(6).toString('hex');
    const schema = `receipt_bench_${suffix}`;
    const collection = `receipts_bench_${suffix}`;
    const pg = new PostgresArchive(client, schema);
    const mongo = new MongoArchive(mongoDb, collection);
    let sqlCreated = false;
    let mongoCreated = false;
    try {
      await client.query(`CREATE SCHEMA ${schema}`);
      sqlCreated = true;
      // Create explicitly so cleanup belongs only to this specific run.
      await mongo.init({ fresh: true, onCreate: () => { mongoCreated = true; } });
      await pg.init();
      let itemCount = 0;
      let paymentCount = 0;
      for (const batch of batches(generate(size), 500)) {
        for (const row of batch) {
          validateReceipt(row);
          itemCount += row.items.length;
          paymentCount += row.payments.length;
        }
        assert.equal(await pg.insert(batch), batch.length);
        assert.equal(await mongo.insert(batch), batch.length);
      }
      await pg.analyze();
      assert.equal(await pg.count(), size);
      assert.equal(await mongo.count(), size);
      const queries = [];
      for (const work of workloads(size, samples)) {
        const query = await measure(pg, mongo, work);
        queries.push(query);
        console.log(`${size} | ${query.label} | SQL ${query.postgres.medianMs} / ${query.postgres.p95Ms} ms | MongoDB ${query.mongo.medianMs} / ${query.mongo.p95Ms} ms`);
      }
      result.datasets.push({ size, itemCount, paymentCount, resultsMatch: true, queries });
    } finally {
      const cleanup = [];
      if (mongoCreated) cleanup.push(mongo.collection.drop());
      if (sqlCreated) cleanup.push(client.query(`DROP SCHEMA ${schema} CASCADE`));
      const states = await Promise.allSettled(cleanup);
      for (const failure of states.filter((state) => state.status === 'rejected')) {
        console.warn(`Could not clean benchmark namespace ${suffix}: ${failure.reason.message}`);
      }
    }
  }
  return result;
}
