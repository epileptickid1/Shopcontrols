-- Insert an entire batch atomically. The first archived copy for a sale ID wins.
WITH source AS (
    SELECT * FROM jsonb_to_recordset($1::jsonb)
        AS r(id text, number text, "soldAt" timestamptz, "totalMinor" bigint, items jsonb, payments jsonb)
), inserted AS (
    INSERT INTO __SCHEMA__.receipts (sale_id, receipt_number, sold_at, total_minor)
    SELECT id, number, "soldAt", "totalMinor" FROM source
    ON CONFLICT (sale_id) DO NOTHING
    RETURNING sale_id
), items AS (
    INSERT INTO __SCHEMA__.receipt_items
        (sale_id, position, product_id, product_name, department_id, department_name,
         quantity_milli, unit_price_minor, discount_minor, line_total_minor)
    SELECT s.id, i.position, i."productId", i."productName", i."departmentId", i."departmentName",
           i."quantityMilli", i."unitPriceMinor", i."discountMinor", i."lineTotalMinor"
    FROM source s JOIN inserted r ON r.sale_id = s.id
    CROSS JOIN LATERAL jsonb_to_recordset(s.items) AS i(
        position integer, "productId" text, "productName" text, "departmentId" text,
        "departmentName" text, "quantityMilli" bigint, "unitPriceMinor" bigint,
        "discountMinor" bigint, "lineTotalMinor" bigint)
), payments AS (
    INSERT INTO __SCHEMA__.receipt_payments (sale_id, position, method, amount_minor)
    SELECT s.id, p.position, p.method, p."amountMinor"
    FROM source s JOIN inserted r ON r.sale_id = s.id
    CROSS JOIN LATERAL jsonb_to_recordset(s.payments)
        AS p(position integer, method text, "amountMinor" bigint)
)
SELECT count(*)::int AS inserted FROM inserted;
