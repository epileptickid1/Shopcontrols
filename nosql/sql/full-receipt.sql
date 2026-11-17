SELECT jsonb_build_object(
    'id', r.sale_id, 'number', r.receipt_number,
    'soldAt', to_char(r.sold_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"'),
    'totalMinor', r.total_minor,
    'items', coalesce((
        SELECT jsonb_agg(jsonb_build_object(
            'position', i.position, 'productId', i.product_id, 'productName', i.product_name,
            'departmentId', i.department_id, 'departmentName', i.department_name,
            'quantityMilli', i.quantity_milli, 'unitPriceMinor', i.unit_price_minor,
            'discountMinor', i.discount_minor, 'lineTotalMinor', i.line_total_minor
        ) ORDER BY i.position)
        FROM __SCHEMA__.receipt_items i WHERE i.sale_id = r.sale_id
    ), '[]'::jsonb),
    'payments', coalesce((
        SELECT jsonb_agg(jsonb_build_object(
            'position', p.position, 'method', p.method, 'amountMinor', p.amount_minor
        ) ORDER BY p.position)
        FROM __SCHEMA__.receipt_payments p WHERE p.sale_id = r.sale_id
    ), '[]'::jsonb)
) AS receipt
FROM __SCHEMA__.receipts r
WHERE __LOOKUP__;
