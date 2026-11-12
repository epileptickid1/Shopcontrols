import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';
import assert from 'node:assert/strict';

const context = {};
runInNewContext(readFileSync(new URL('../app/Store.Web/wwwroot/js/money.js', import.meta.url), 'utf8'), context);
const money = context.StoreMoney;

// PostgreSQL NUMERIC rounds each line before the document is summed.
assert.equal(money.lineCents('0.5', '19.99'), 1000n);
assert.equal(money.lineCents('1.25', '8.06'), 1008n);
assert.equal(money.lineCents('0.005', '1'), 1n);
assert.equal(money.lineCents('0.004', '1'), 0n);
assert.equal(money.lineCents('0.001', '0.01'), 0n);
assert.equal(money.cents('10.075'), 1008n);
assert.equal(money.cents('-10.075'), -1008n);
assert.equal(money.lineCents('1e-3', '1e3'), 100n);

const subtotal = money.lineCents('0.5', '19.99') + money.lineCents('1.25', '8.06');
const discounted = subtotal - money.cents('0.08');
assert.equal(discounted, 2000n);
assert.equal(discounted - money.cents('3.27') - money.cents('16.73'), 0n);
assert.equal(money.formatInput(discounted - money.cents('3.27')), '16.73');
assert.equal(money.formatInput(money.cents('0.01')), '0.01');
assert.equal(money.formatInput(money.cents('-0.01')), '-0.01');
assert.equal(money.lineCents('99999999999.999', '9999999999.99'), 99999999999899000000000n);

// Empty and incomplete number inputs should not break the remaining preview.
assert.equal(money.cents(''), 0n);
assert.equal(money.cents('bad input'), 0n);
assert.equal(money.cents('1e9999'), 0n);
assert.equal(money.cents('1e-999999999'), 0n);
assert.equal(money.formatCurrency(1008n).replace(/\s/g, ''), '10,08₴');
assert.equal(money.formatCurrency(-1n).replace(/\s/g, ''), '-0,01₴');
assert.equal(money.formatCurrency(99999999999899000000001n).replace(/\s/g, ''), '999999999998990000000,01₴');
process.stdout.write('21 money preview assertions passed.\n');

