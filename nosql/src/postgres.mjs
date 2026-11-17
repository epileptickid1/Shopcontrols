import { readFileSync } from 'node:fs';

export const sql = (name) => readFileSync(new URL(`../sql/${name}.sql`, import.meta.url), 'utf8').trim();

export function archiveSql(file, schema) {
  if (!/^(receipt_archive|receipt_bench_[a-f0-9]{12})$/.test(schema)) {
    throw new Error('Unexpected archive SQL schema name.');
  }
  return sql(file).replaceAll('__SCHEMA__', schema);
}

export const sqlDate = (value) => value instanceof Date ? value.toISOString() : new Date(value).toISOString();

export function toSafeNumber(value) {
  const n = Number(value);
  if (!Number.isSafeInteger(n)) throw new Error('Result exceeds exact integer range.');
  return n;
}

export class PostgresArchive {
  constructor(client, schema = 'receipt_archive') {
    this.client = client;
    this.schema = schema;
    this.fullSql = archiveSql('full-receipt', schema).replace('__LOOKUP__', 'r.receipt_number = $1');
    this.idsSql = archiveSql('full-receipt', schema).replace('__LOOKUP__', 'r.sale_id = ANY($1::text[])');
    this.productSql = archiveSql('by-product', schema);
    this.paymentsSql = archiveSql('payments-by-method', schema);
    this.insertSql = archiveSql('insert', schema);
  }

  async init() {
    await this.client.query(archiveSql('schema', this.schema));
  }

  async insert(rows) {
    const result = await this.client.query(this.insertSql, [JSON.stringify(rows)]);
    return result.rows[0].inserted;
  }

  async byIds(ids) {
    const result = await this.client.query(this.idsSql, [ids]);
    return result.rows.map((r) => r.receipt);
  }

  async full(number) {
    const { rows } = await this.client.query(this.fullSql, [number]);
    return rows[0]?.receipt ?? null;
  }

  async byProduct(id) {
    const { rows } = await this.client.query(this.productSql, [id]);
    return rows.map((r) => ({ ...r, soldAt: sqlDate(r.soldAt), totalMinor: toSafeNumber(r.totalMinor) }));
  }

  async paymentsByMethod(from, to) {
    const { rows } = await this.client.query(this.paymentsSql, [from, to]);
    return rows.map((r) => ({ method: r.method, amountMinor: toSafeNumber(r.amountMinor) }));
  }

  async analyze() {
    for (const table of ['receipts', 'receipt_items', 'receipt_payments']) {
      await this.client.query(`ANALYZE ${this.schema}.${table}`);
    }
  }

  async count() {
    const { rows } = await this.client.query(`SELECT count(*) FROM ${this.schema}.receipts`);
    return toSafeNumber(rows[0].count);
  }

  async explain(operation, args) {
    const query = { full: this.fullSql, byProduct: this.productSql, paymentsByMethod: this.paymentsSql }[operation];
    const { rows } = await this.client.query(`EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) ${query}`, args);
    return rows[0]['QUERY PLAN'][0];
  }
}
