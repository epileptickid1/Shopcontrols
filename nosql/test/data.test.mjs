import assert from 'node:assert/strict';
import test from 'node:test';
import { latency } from '../src/benchmark.mjs';
import { batches, demoReceipts, generate, validateReceipt } from '../src/data.mjs';
import { fromMongo, toMongo } from '../src/mongo.mjs';

test('generated datasets are reproducible and totals reconcile on 100,000 receipts', () => {
  assert.deepEqual([...generate(12)], [...generate(20)].slice(0, 12));
  const ids = new Set();
  const methods = new Set();
  for (const row of generate(100_000)) {
    validateReceipt(row);
    ids.add(row.id);
    row.payments.forEach((payment) => methods.add(payment.method));
  }
  assert.equal(ids.size, 100_000);
  assert.equal(methods.size, 3);
});

test('archived documents round-trip without losing money, fractional units or dates', () => {
  for (const row of demoReceipts()) {
    validateReceipt(row);
    const doc = toMongo(row);
    assert.ok(doc.soldAt instanceof Date);
    assert.deepEqual(fromMongo(doc), row);
  }
  const invalid = demoReceipts()[0];
  invalid.payments[0].amountMinor++;
  assert.throws(() => validateReceipt(invalid), { code: 'ERR_ASSERTION' });
});

test('last partial batch and the median and p95 of an even sample are preserved', () => {
  assert.deepEqual([...batches([1, 2, 3, 4, 5], 2)], [[1, 2], [3, 4], [5]]);
  assert.deepEqual(latency(Array.from({ length: 20 }, (_, i) => 20 - i)), { medianMs: 10.5, p95Ms: 19 });
});
