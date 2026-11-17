import { toSafeNumber } from './postgres.mjs';

const int = (minimum) => ({ bsonType: ['int', 'long', 'double'], minimum, multipleOf: 1 });
const string = { bsonType: 'string', minLength: 1 };
const fields = (properties) => ({ bsonType: 'object', required: Object.keys(properties), properties });
const validator = {
  $jsonSchema: fields({
    _id: string, number: string, soldAt: { bsonType: 'date' }, totalMinor: int(0),
    items: { bsonType: 'array', minItems: 1, maxItems: 100, items: fields({
      position: int(1), productId: string, productName: string, departmentId: string, departmentName: string,
      quantityMilli: int(1), unitPriceMinor: int(0), discountMinor: int(0), lineTotalMinor: int(0)
    }) },
    payments: { bsonType: 'array', maxItems: 20, items: fields({
      position: int(1), method: { enum: ['cash', 'card', 'bank_transfer'] }, amountMinor: int(1)
    }) }
  })
};

export function fromMongo({ _id, soldAt, ...data }) {
  return { id: _id, ...data, soldAt: soldAt.toISOString() };
}

export function toMongo({ id, soldAt, ...data }) {
  return { _id: id, ...data, soldAt: new Date(soldAt) };
}

export function paymentPipeline(from, to) {
  return [
    { $match: { soldAt: { $gte: new Date(from), $lt: new Date(to) } } },
    { $unwind: '$payments' },
    { $group: { _id: '$payments.method', amountMinor: { $sum: '$payments.amountMinor' } } },
    { $sort: { _id: 1 } },
    { $project: { _id: 0, method: '$_id', amountMinor: 1 } }
  ];
}

export class MongoArchive {
  constructor(db, name = 'receipts') {
    this.db = db;
    this.name = name;
    this.collection = db.collection(name);
  }

  async init({ fresh = false, onCreate = () => {} } = {}) {
    try {
      await this.db.createCollection(this.name, { validator });
      onCreate();
    } catch (error) {
      if (fresh || error.code !== 48) throw error; // Namespace already exists after a previous import.
    }
    await this.collection.createIndexes([
      { key: { number: 1 }, unique: true, name: 'receipt_number' },
      { key: { soldAt: -1, number: 1 }, name: 'receipt_date' },
      { key: { 'items.productId': 1, soldAt: -1, number: 1 }, name: 'receipt_product_date' }
    ]);
  }

  async insert(rows) {
    if (!rows.length) return 0;
    const operations = rows.map((row) => {
      const { _id, ...payload } = toMongo(row);
      return { updateOne: { filter: { _id }, update: { $setOnInsert: payload }, upsert: true } };
    });
    const result = await this.collection.bulkWrite(operations);
    return result.upsertedCount;
  }

  async full(number) {
    const row = await this.collection.findOne({ number });
    return row ? fromMongo(row) : null;
  }

  productCursor(id) {
    return this.collection.find({ 'items.productId': id }, { projection: {
      _id: 0, number: 1, soldAt: 1, totalMinor: 1
    } }).sort({ soldAt: -1, number: 1 }).limit(50);
  }

  async byProduct(id) {
    const rows = await this.productCursor(id).toArray();
    return rows.map((r) => ({ ...r, soldAt: r.soldAt.toISOString(), totalMinor: toSafeNumber(r.totalMinor) }));
  }

  async paymentsByMethod(from, to) {
    const rows = await this.collection.aggregate(paymentPipeline(from, to)).toArray();
    return rows.map((r) => ({ method: r.method, amountMinor: toSafeNumber(r.amountMinor) }));
  }

  async count() {
    return this.collection.countDocuments();
  }

  async explain(operation, args) {
    if (operation === 'full') return this.collection.find({ number: args[0] }).limit(1).explain('executionStats');
    if (operation === 'byProduct') return this.productCursor(args[0]).explain('executionStats');
    return this.collection.aggregate(paymentPipeline(...args)).explain('executionStats');
  }
}
