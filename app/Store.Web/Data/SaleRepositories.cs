using Npgsql;
using Store.Web.Models;

namespace Store.Web.Data;

public sealed class SaleRepository(PostgreSqlUnitOfWork unit) : ISaleRepository
{
    private const string Columns =
        "id, receipt_number, customer_id, customer_name, cashier, sold_at, status, total, paid, balance";
    private static SaleSummary Map(NpgsqlDataReader r) => new(
        r.GetInt64(0), r.GetString(1), SqlValue.NullableId(r, 2), SqlValue.NullableString(r, 3),
        r.GetString(4), r.GetDateTime(5), r.GetString(6), r.GetDecimal(7), r.GetDecimal(8), r.GetDecimal(9));

    public Task<IReadOnlyList<SaleSummary>> ListAsync(CancellationToken ct = default) =>
        unit.QueryAsync(
            $"SELECT {Columns} FROM store.v_sales_summary ORDER BY sold_at DESC, id DESC LIMIT 200", Map, ct);

    public async Task<SaleSummary?> GetAsync(long id, CancellationToken ct = default) =>
        (await unit.QueryAsync($"SELECT {Columns} FROM store.v_sales_summary WHERE id = $1",
            Map, ct, SqlValue.Id(id))).SingleOrDefault();

    public Task<IReadOnlyList<SaleLine>> LinesAsync(long id, CancellationToken ct = default) =>
        unit.QueryAsync(
            "SELECT id, department_id, product_id, department_name, product_name, sku, unit, quantity, unit_price, discount_amount, line_total " +
            "FROM store.v_sale_items WHERE sale_id = $1 ORDER BY id",
            r => new SaleLine(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetString(3),
                r.GetString(4), r.GetString(5), r.GetString(6), r.GetDecimal(7), r.GetDecimal(8),
                r.GetDecimal(9), r.GetDecimal(10)), ct, SqlValue.Id(id));

    public Task<long> CreateAsync(string receipt, long? customerId, CancellationToken ct = default) =>
        unit.CreateAsync("CALL store.sp_create_sale($1, $2, $3)", ct,
            SqlValue.Text(receipt), SqlValue.Id(customerId), SqlValue.Id(null));

    public Task AddLineAsync(long id, SaleLineInput line, CancellationToken ct = default) =>
        unit.ExecuteAsync("CALL store.sp_add_sale_item($1, $2, $3, $4, $5)", ct,
            SqlValue.Id(id), SqlValue.Id(line.DepartmentId), SqlValue.Id(line.ProductId),
            SqlValue.Number(line.Quantity), SqlValue.Number(line.DiscountAmount));

    public Task PostAsync(long id, CancellationToken ct = default) =>
        unit.ExecuteAsync("CALL store.sp_post_sale($1)", ct, SqlValue.Id(id));
}

public sealed class PaymentRepository(PostgreSqlUnitOfWork unit) : IPaymentRepository
{
    public Task<IReadOnlyList<PaymentInfo>> ListAsync(long saleId, CancellationToken ct = default) =>
        unit.QueryAsync(
            "SELECT id, method, amount, paid_at, transaction_reference FROM store.v_payments WHERE sale_id = $1 ORDER BY id",
            r => new PaymentInfo(r.GetInt64(0), r.GetString(1), r.GetDecimal(2),
                r.GetDateTime(3), SqlValue.NullableString(r, 4)), ct, SqlValue.Id(saleId));

    public Task AddAsync(long saleId, PaymentInput payment, CancellationToken ct = default) =>
        unit.ExecuteAsync("CALL store.sp_add_payment($1, $2, $3, $4)", ct,
            SqlValue.Id(saleId), SqlValue.Text(payment.Method), SqlValue.Number(payment.Amount),
            SqlValue.Text(string.IsNullOrWhiteSpace(payment.Reference) ? null : payment.Reference));
}

