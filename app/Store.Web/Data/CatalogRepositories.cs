using Npgsql;
using Store.Web.Models;

namespace Store.Web.Data;

public sealed class ProductRepository(PostgreSqlUnitOfWork unit) : IProductRepository
{
    private const string Columns =
        "id, category_id, category_name, sku, barcode, name, unit, sale_price, updated_at, updated_by";

    private static Product Map(NpgsqlDataReader r) => new(
        r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), SqlValue.NullableString(r, 4),
        r.GetString(5), r.GetString(6), r.GetDecimal(7), r.GetDateTime(8), r.GetInt64(9));

    public Task<IReadOnlyList<Product>> ListAsync(string? search = null, CancellationToken ct = default)
    {
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        return unit.QueryAsync(
            $"SELECT {Columns} FROM store.v_active_products " +
            "WHERE $1 IS NULL OR strpos(lower(name), lower($1)) > 0 OR strpos(lower(sku), lower($1)) > 0 " +
            "ORDER BY name, id", Map, ct, SqlValue.Text(term));
    }

    public async Task<Product?> GetAsync(long id, CancellationToken ct = default) =>
        (await unit.QueryAsync(
            $"SELECT {Columns} FROM store.v_active_products WHERE id = $1",
            Map, ct, SqlValue.Id(id))).SingleOrDefault();

    public Task<IReadOnlyList<ArchivedProduct>> ArchiveAsync(CancellationToken ct = default) =>
        unit.QueryAsync(
            "SELECT id, sku, name, deleted_at, updated_at, updated_by FROM store.v_product_archive ORDER BY deleted_at DESC, id",
            r => new ArchivedProduct(r.GetInt64(0), r.GetString(1), r.GetString(2),
                r.GetDateTime(3), r.GetDateTime(4), r.GetInt64(5)), ct);

    public Task<long> SaveAsync(ProductInput input, CancellationToken ct = default) =>
        unit.CreateAsync("CALL store.sp_save_product($1, $2, $3, $4, $5, $6, $7)", ct,
            SqlValue.Id(input.CategoryId), SqlValue.Text(input.Sku), SqlValue.Text(input.Name),
            SqlValue.Text(input.Unit), SqlValue.Number(input.SalePrice), SqlValue.Id(input.Id),
            SqlValue.Text(string.IsNullOrWhiteSpace(input.Barcode) ? null : input.Barcode));

    public Task DeleteAsync(long id, CancellationToken ct = default) =>
        unit.ExecuteAsync("CALL store.sp_soft_delete_product($1)", ct, SqlValue.Id(id));

    public Task RestoreAsync(long id, CancellationToken ct = default) =>
        unit.ExecuteAsync("CALL store.sp_restore_product($1)", ct, SqlValue.Id(id));

    public Task SetSupplierAsync(SupplierProductInput input, CancellationToken ct = default) =>
        unit.ExecuteAsync("CALL store.sp_set_supplier_product($1, $2, $3)", ct,
            SqlValue.Id(input.ProductId), SqlValue.Id(input.SupplierId), SqlValue.Number(input.PurchasePrice));
}

public sealed class InventoryRepository(PostgreSqlUnitOfWork unit) : IInventoryRepository
{
    public Task<IReadOnlyList<Stock>> ListAsync(
        long? departmentId = null, bool onlyLow = false, CancellationToken ct = default) =>
        unit.QueryAsync(
            "SELECT department_id, department_name, product_id, product_name, sku, unit, quantity, minimum_quantity, needs_restock " +
            "FROM store.v_inventory WHERE ($1 IS NULL OR department_id = $1) AND (NOT $2 OR needs_restock) " +
            "ORDER BY department_name, product_name, product_id",
            r => new Stock(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetString(3),
                r.GetString(4), r.GetString(5), r.GetDecimal(6), r.GetDecimal(7), r.GetBoolean(8)),
            ct, SqlValue.Id(departmentId), SqlValue.Flag(onlyLow));

    public Task SetMinimumAsync(long departmentId, long productId, decimal minimum, CancellationToken ct = default) =>
        unit.ExecuteAsync("CALL store.sp_set_minimum_stock($1, $2, $3)", ct,
            SqlValue.Id(departmentId), SqlValue.Id(productId), SqlValue.Number(minimum));
}

public sealed class LookupRepository(PostgreSqlUnitOfWork unit) : ILookupRepository
{
    public Task<IReadOnlyList<ManagerProfile>> ManagersAsync(CancellationToken ct = default) =>
        unit.QueryAsync("SELECT id, username, full_name, role_name, updated_at, updated_by FROM store.v_manager_profiles ORDER BY full_name, id",
            r => new ManagerProfile(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetDateTime(4), r.GetInt64(5)), ct);

    public Task RenameCurrentManagerAsync(string fullName, CancellationToken ct = default) =>
        unit.ExecuteAsync("CALL store.sp_rename_current_manager($1)", ct, SqlValue.Text(fullName));

    public Task<IReadOnlyList<Lookup>> CategoriesAsync(CancellationToken ct = default) =>
        ReadLookups("SELECT id, name FROM store.v_active_categories ORDER BY name, id", ct);
    public Task<IReadOnlyList<Lookup>> DepartmentsAsync(CancellationToken ct = default) =>
        ReadLookups("SELECT id, name FROM store.v_active_departments ORDER BY name, id", ct);
    public Task<IReadOnlyList<Lookup>> CustomersAsync(CancellationToken ct = default) =>
        ReadLookups("SELECT id, full_name FROM store.v_active_customers ORDER BY full_name, id", ct);
    public Task<IReadOnlyList<Lookup>> SuppliersAsync(CancellationToken ct = default) =>
        ReadLookups("SELECT id, name FROM store.v_active_suppliers ORDER BY name, id", ct);

    private Task<IReadOnlyList<Lookup>> ReadLookups(string sql, CancellationToken ct) =>
        unit.QueryAsync(sql, r => new Lookup(r.GetInt64(0), r.GetString(1)), ct);

    public Task<IReadOnlyList<SupplierProduct>> SupplierProductsAsync(CancellationToken ct = default) =>
        unit.QueryAsync(
            "SELECT product_id, supplier_id, product_name, supplier_name, purchase_price " +
            "FROM store.v_supplier_products ORDER BY supplier_name, product_name, product_id",
            r => new SupplierProduct(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetDecimal(4)), ct);

    public async Task<CurrentUser?> CurrentUserAsync(CancellationToken ct = default) =>
        (await unit.QueryAsync(
            "SELECT id, username, full_name, role_name FROM store.v_current_user",
            r => new CurrentUser(r.GetInt64(0), r.GetString(1), SqlValue.NullableString(r, 2), r.GetString(3)), ct))
        .SingleOrDefault();
}

