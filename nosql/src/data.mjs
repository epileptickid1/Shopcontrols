import assert from 'node:assert/strict';

export const methods = ['bank_transfer', 'card', 'cash'];
const dayMs = 86_400_000;
const start = Date.UTC(2025, 0, 1);
const products = [
  ['1', 'Молоко', '1', 'Продуктовий'],
  ['2', 'Хліб', '1', 'Продуктовий'],
  ['3', 'Яблука', '1', 'Продуктовий'],
  ['4', 'Чай', '2', 'Напої'],
  ['5', 'Кава', '2', 'Напої']
];

export function random(seed) {
  let value = seed >>> 0;
  return () => {
    value = (Math.imul(1664525, value) + 1013904223) >>> 0;
    return value / 2 ** 32;
  };
}

function item(position, product, quantityMilli, unitPriceMinor, discountMinor = 0) {
  const [productId, productName, departmentId, departmentName] = product;
  const rounded = Math.floor((quantityMilli * unitPriceMinor + 500) / 1000);
  return { position, productId, productName, departmentId, departmentName,
    quantityMilli, unitPriceMinor, discountMinor, lineTotalMinor: rounded - discountMinor };
}

function receipt(id, number, date, items, split, method) {
  const totalMinor = items.reduce((sum, line) => sum + line.lineTotalMinor, 0);
  const payments = split
    ? [{ position: 1, method: 'cash', amountMinor: Math.floor(totalMinor / 2) },
       { position: 2, method: 'card', amountMinor: Math.ceil(totalMinor / 2) }]
    : [{ position: 1, method, amountMinor: totalMinor }];
  return { id, number, soldAt: date.toISOString(), totalMinor, items, payments };
}

export function demoReceipts() {
  const baskets = [[0, 1, 2], [0, 1], [0, 3], [1, 4]];
  return baskets.map((basket, i) => receipt(
    `demo-${i + 1}`, `NO-SQL-DEMO-${i + 1}`, new Date(start + i * dayMs),
    basket.map((id, j) => item(j + 1, products[id], id === 2 ? 1250 : 1000, 2800 + id * 375)),
    i % 2 === 0, methods[i % methods.length]));
}

// Same seed produces the same prefix at every scale. IDs and dates do not use wall-clock time.
export function* generate(count, seed = 20261118) {
  const next = random(seed);
  for (let id = 1; id <= count; id++) {
    const lineCount = 2 + Math.floor(next() * 7);
    const selected = new Set();
    const lines = [];
    while (lines.length < lineCount) {
      const product = 1 + Math.floor(next() ** 2 * 2000);
      if (selected.has(product)) continue;
      selected.add(product);
      const department = 1 + product % 12;
      const quantity = (1 + Math.floor(next() * 5)) * (product % 4 ? 1000 : 250);
      const price = 300 + Math.floor(next() * 20_000);
      const discount = next() < 0.12 ? 25 : 0;
      lines.push(item(lines.length + 1,
        [String(product), `Товар ${product}`, String(department), `Відділ ${department}`],
        quantity, price, discount));
    }
    const date = new Date(start + Math.floor(next() * 365 * dayMs));
    yield receipt(String(id), benchmarkNumber(id), date, lines, next() < 0.28,
      methods[Math.floor(next() * methods.length)]);
  }
}

export function benchmarkNumber(id) {
  return `BENCH-${String(id).padStart(8, '0')}`;
}

export function* batches(iterable, count) {
  let batch = [];
  for (const entry of iterable) {
    batch.push(entry);
    if (batch.length === count) {
      yield batch;
      batch = [];
    }
  }
  if (batch.length) yield batch;
}

export function validateReceipt(value) {
  assert.ok(value.id && value.number);
  assert.equal(new Date(value.soldAt).toISOString(), value.soldAt);
  const integer = (n, min = 0) => assert.ok(Number.isSafeInteger(n) && n >= min);
  integer(value.totalMinor);
  assert.ok(value.items.length > 0 && value.items.length <= 100);
  assert.ok(value.payments.length <= 20);
  for (const [i, line] of value.items.entries()) {
    assert.equal(line.position, i + 1);
    assert.ok(line.productId && line.productName && line.departmentId && line.departmentName);
    integer(line.quantityMilli, 1);
    integer(line.unitPriceMinor);
    integer(line.discountMinor);
    integer(line.lineTotalMinor);
    const rounded = (BigInt(line.quantityMilli) * BigInt(line.unitPriceMinor) + 500n) / 1000n;
    assert.equal(rounded - BigInt(line.discountMinor), BigInt(line.lineTotalMinor));
  }
  for (const [i, payment] of value.payments.entries()) {
    assert.equal(payment.position, i + 1);
    assert.ok(methods.includes(payment.method));
    integer(payment.amountMinor, 1);
  }
  assert.equal(value.items.reduce((sum, i) => sum + i.lineTotalMinor, 0), value.totalMinor);
  assert.equal(value.payments.reduce((sum, p) => sum + p.amountMinor, 0), value.totalMinor);
}
