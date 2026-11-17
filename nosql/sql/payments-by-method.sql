SELECT p.method, sum(p.amount_minor) AS "amountMinor"
FROM __SCHEMA__.receipts r
JOIN __SCHEMA__.receipt_payments p USING (sale_id)
WHERE r.sold_at >= $1::timestamptz AND r.sold_at < $2::timestamptz
GROUP BY p.method
ORDER BY p.method;
