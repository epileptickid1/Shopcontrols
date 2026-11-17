import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fromMongo } from './mongo.mjs';
import { demoReceipts } from './data.mjs';

const cypher = (name) => readFileSync(new URL(`../neo4j/${name}.cypher`, import.meta.url), 'utf8').trim();

export async function runGraph(driver, collection, { dataset, receiptId, productId }) {
  const session = driver.session({ database: 'neo4j' });
  try {
    for (const statement of cypher('schema').split(';').map((s) => s.trim()).filter(Boolean)) {
      await session.run(statement);
    }
    const upload = (rows) => session.executeWrite((tx) => tx.run(cypher('import'), { dataset, rows }));
    let rows = [];
    let receiptCount = 0;
    let first;
    const idPattern = dataset === 'demo' ? /^demo-/ : /^\d+$/;
    for await (const entry of collection.find({ _id: idPattern }).sort({ _id: 1 })) {
      const receipt = fromMongo(entry);
      first ??= receipt;
      rows.push(receipt);
      receiptCount++;
      if (rows.length === 200) {
        await upload(rows);
        rows = [];
      }
    }
    if (rows.length) await upload(rows);
    if (!first) throw new Error(`No receipts for graph dataset "${dataset}". Run demo or import first.`);
    receiptId ??= first.id;
    productId ??= first.items[0].productId;

    const query = async (name, params) => {
      const result = await session.run(cypher(name), { dataset, ...params });
      return result.records.map((record) => record.toObject());
    };
    const items = await query('items-of-receipt', { receiptId });
    const count = await query('count-receipts', { productId });
    const together = await query('bought-together', { productId });
    if (dataset === 'demo' && receiptId === 'demo-1' && productId === '1') {
      const expected = demoReceipts();
      assert.deepEqual(items.map((line) => line.productId), expected[0].items.map((line) => line.productId));
      assert.equal(count[0]?.receipts, 3);
      assert.deepEqual(together.map(({ productId, sharedReceipts }) => [productId, sharedReceipts]),
        [['2', 2], ['3', 1], ['4', 1]]);
    }
    return { executedAt: new Date().toISOString(), dataset, receiptCount, receiptId, productId, items, count, together };
  } finally {
    await session.close();
  }
}
