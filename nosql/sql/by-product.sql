SELECT r.receipt_number AS number, r.sold_at AS "soldAt", r.total_minor AS "totalMinor"
FROM __SCHEMA__.receipts r
WHERE EXISTS (
    SELECT 1 FROM __SCHEMA__.receipt_items i
    WHERE i.product_id = $1 AND i.sale_id = r.sale_id
)
ORDER BY r.sold_at DESC, r.receipt_number ASC
LIMIT 50;
