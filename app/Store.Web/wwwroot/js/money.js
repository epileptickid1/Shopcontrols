(() => {
 'use strict';
 // Keep decimal input exact until the final formatted display; PostgreSQL also rounds ties away from zero.
 function decimal(value) {
  const text = String(value ?? '').trim();
  const match = /^([+-]?)(\d*)(?:\.(\d*))?(?:[eE]([+-]?\d+))?$/.exec(text);
  if (!match || !(match[2] || match[3]) || (!Number.isFinite(Number(text)) || Number(text) === 0)) return {units:0n, scale:0};
  const fraction = match[3] || '';
  return {
   units: BigInt((match[1] === '-' ? '-' : '') + (match[2] || '0') + fraction),
   scale: fraction.length - Number(match[4] || 0)
  };
 }
 function roundToCents(units, scale) {
  const shift = 2 - scale;
  if (shift >= 0) return units * 10n ** BigInt(shift);
  const divisor = 10n ** BigInt(-shift);
  const absolute = units < 0n ? -units : units;
  const rounded = (absolute + divisor / 2n) / divisor;
  return units < 0n ? -rounded : rounded;
 }
 function cents(value) {
  const number = decimal(value);
  return roundToCents(number.units, number.scale);
 }
 function lineCents(quantity, price) {
  const q = decimal(quantity), p = decimal(price);
  return roundToCents(q.units * p.units, q.scale + p.scale);
 }
 function formatInput(value) {
  const absolute = value < 0n ? -value : value;
  return (value < 0n ? '-' : '') + absolute / 100n + '.' + String(absolute % 100n).padStart(2, '0');
 }
 const wholeMoney = new Intl.NumberFormat('uk-UA', {maximumFractionDigits:0});
 function formatCurrency(value) {
  const absolute = value < 0n ? -value : value;
  return (value < 0n ? '-' : '') + wholeMoney.format(absolute / 100n) + ',' + String(absolute % 100n).padStart(2, '0') + ' ₴';
 }
 globalThis.StoreMoney = Object.freeze({cents, lineCents, formatInput, formatCurrency});
})();


