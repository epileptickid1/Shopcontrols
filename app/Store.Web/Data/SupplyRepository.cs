using Npgsql;
using Store.Web.Models;

namespace Store.Web.Data;

public sealed class SupplyRepository(PostgreSqlUnitOfWork unit) : ISupplyRepository
{
    private const string Columns =
        "id, supplier_id, supplier_name, document_number, received_at, status, total";
    private static SupplySummary Map(NpgsqlDataReader r) => new(
        r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetDateTime(4), r.GetString(5), r.GetDecimal(6));

    public Task<IReadOnlyList<SupplySummary>> ListAsync(CancellationToken ct = default) =>
        unit.QueryAsync(
            $"SELECT {Columns} FROM store.v_supplies_summary ORDER BY received_at DESC, id DESC LIMIT 200", Map, ct);

    public async Task<SupplySummary?> GetAsync(long id, CancellationToken ct = default) =>
        (await unit.QueryAsync($"SELECT {Columns} FROM store.v_supplies_summary WHERE id = $1",
            Map, ct, SqlValue.Id(id))).SingleOrDefault();

    public Task<IReadOnlyList<SupplyLine>> LinesAsync(long id, CancellationToken ct = default) =>
        unit.QueryAsync(
            "SELECT id, department_id, product_id, department_name, product_name, quantity, unit_cost, line_total " +
            "FROM store.v_supply_items WHERE supply_id = $1 ORDER BY id",
            r => new SupplyLine(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetString(3),
                r.GetString(4), r.GetDecimal(5), r.GetDecimal(6), r.GetDecimal(7)), ct, SqlValue.Id(id));

    public Task<long> CreateAsync(long supplierId, string document, CancellationToken ct = default) =>
        unit.CreateAsync("CALL store.sp_create_supply($1, $2, $3)", ct,
            SqlValue.Id(supplierId), SqlValue.Text(document), SqlValue.Id(null));

    public Task AddLineAsync(long id, SupplyLineInput line, CancellationToken ct = default) =>
        unit.ExecuteAsync("CALL store.sp_add_supply_item($1, $2, $3, $4, $5)", ct,
            SqlValue.Id(id), SqlValue.Id(line.DepartmentId), SqlValue.Id(line.ProductId),
            SqlValue.Number(line.Quantity), SqlValue.Number(line.UnitCost));

    public Task PostAsync(long id, CancellationToken ct = default) =>
        unit.ExecuteAsync("CALL store.sp_post_supply($1)", ct, SqlValue.Id(id));
}

