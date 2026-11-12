using Store.Web.Models;
namespace Store.Web.Services;

public interface IStoreService
{
 Task<IReadOnlyList<Product>> ProductsAsync(string? search = null, CancellationToken ct = default);
 Task<Product?> ProductAsync(long id, CancellationToken ct = default);
 Task<IReadOnlyList<ArchivedProduct>> ArchiveAsync(CancellationToken ct = default);
 Task<long> SaveProductAsync(ProductInput input, CancellationToken ct = default);
 Task DeleteProductAsync(long id, CancellationToken ct = default);
 Task RestoreProductAsync(long id, CancellationToken ct = default);
 Task SetSupplierProductAsync(SupplierProductInput input, CancellationToken ct = default);
 Task<IReadOnlyList<Stock>> InventoryAsync(long? departmentId = null, bool onlyLow = false, CancellationToken ct = default);
 Task SetMinimumAsync(long departmentId, long productId, decimal minimum, CancellationToken ct = default);
 Task<IReadOnlyList<SaleSummary>> SalesAsync(CancellationToken ct = default);
 Task<SaleDetails?> SaleAsync(long id, CancellationToken ct = default);
 Task<long> CheckoutAsync(CheckoutInput input, CancellationToken ct = default);
 Task<IReadOnlyList<SupplySummary>> SuppliesAsync(CancellationToken ct = default);
 Task<SupplyDetails?> SupplyAsync(long id, CancellationToken ct = default);
 Task<long> ReceiveAsync(SupplyInput input, CancellationToken ct = default);
 Task<IReadOnlyList<Lookup>> CategoriesAsync(CancellationToken ct = default);
 Task<IReadOnlyList<Lookup>> DepartmentsAsync(CancellationToken ct = default);
 Task<IReadOnlyList<Lookup>> CustomersAsync(CancellationToken ct = default);
 Task<IReadOnlyList<Lookup>> SuppliersAsync(CancellationToken ct = default);
 Task<IReadOnlyList<SupplierProduct>> SupplierProductsAsync(CancellationToken ct = default);
 Task<CurrentUser?> CurrentUserAsync(CancellationToken ct = default);
 Task<IReadOnlyList<ManagerProfile>> ManagersAsync(CancellationToken ct = default);
 Task RenameCurrentManagerAsync(ManagerNameInput input, CancellationToken ct = default);
}
public sealed class StoreOperationException(string message, Exception? inner = null) : Exception(message, inner);

