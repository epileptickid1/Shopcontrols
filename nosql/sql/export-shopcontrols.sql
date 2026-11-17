-- Uses the existing application's views. No personal data or payment references are copied.
WITH batch AS (
    SELECT id, receipt_number, sold_at, (total * 100)::bigint AS total_minor
    FROM store.v_sales_summary
    WHERE status = 'posted' AND id > $1::bigint AND ($2::bigint IS NULL OR id = $2::bigint)
    ORDER BY id LIMIT $3::int
)
SELECT jsonb_build_object(
    'id', b.id::text, 'number', b.receipt_number,
    'soldAt', to_char(date_trunc('milliseconds', b.sold_at) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"'),
    'totalMinor', b.total_minor,
    'items', coalesce((
        SELECT jsonb_agg(jsonb_build_object(
            'position', i.position, 'productId', i.product_id::text, 'productName', i.product_name,
            'departmentId', i.department_id::text, 'departmentName', i.department_name,
            'quantityMilli', (i.quantity * 1000)::bigint, 'unitPriceMinor', (i.unit_price * 100)::bigint,
            'discountMinor', (i.discount_amount * 100)::bigint, 'lineTotalMinor', (i.line_total * 100)::bigint
        ) ORDER BY i.position)
        FROM (
            SELECT v.*, row_number() OVER (ORDER BY v.id) AS position
            FROM store.v_sale_items v WHERE v.sale_id = b.id
        ) i
    ), '[]'::jsonb),
    'payments', coalesce((
        SELECT jsonb_agg(jsonb_build_object(
            'position', p.position, 'method', p.method, 'amountMinor', (p.amount * 100)::bigint
        ) ORDER BY p.position)
        FROM (
            SELECT v.*, row_number() OVER (ORDER BY v.id) AS position
            FROM store.v_payments v WHERE v.sale_id = b.id
        ) p
    ), '[]'::jsonb)
) AS receipt
FROM batch b ORDER BY b.id;
