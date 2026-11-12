using Store.Web.Models;

namespace Store.Web.Data;

public interface IProductRepository
{
 Task<IReadOnlyList<Product>> ListAsync(string? search = null, CancellationToken ct = default);
 Task<Product?> GetAsync(long id, CancellationToken ct = default);
 Task<IReadOnlyList<ArchivedProduct>> ArchiveAsync(CancellationToken ct = default);
 Task<long> SaveAsync(ProductInput input, CancellationToken ct = default);
 Task DeleteAsync(long id, CancellationToken ct = default);
 Task RestoreAsync(long id, CancellationToken ct = default);
 Task SetSupplierAsync(SupplierProductInput input, CancellationToken ct = default);
}
public interface IInventoryRepository
{
 Task<IReadOnlyList<Stock>> ListAsync(long? departmentId = null, bool onlyLow = false, CancellationToken ct = default);
 Task SetMinimumAsync(long departmentId, long productId, decimal minimum, CancellationToken ct = default);
}
public interface ISaleRepository
{
 Task<IReadOnlyList<SaleSummary>> ListAsync(CancellationToken ct = default);
 Task<SaleSummary?> GetAsync(long id, CancellationToken ct = default);
 Task<IReadOnlyList<SaleLine>> LinesAsync(long id, CancellationToken ct = default);
 Task<long> CreateAsync(string receipt, long? customerId, CancellationToken ct = default);
 Task AddLineAsync(long id, SaleLineInput line, CancellationToken ct = default);
 Task PostAsync(long id, CancellationToken ct = default);
}
public interface IPaymentRepository
{
 Task<IReadOnlyList<PaymentInfo>> ListAsync(long saleId, CancellationToken ct = default);
 Task AddAsync(long saleId, PaymentInput payment, CancellationToken ct = default);
}
public interface ISupplyRepository
{
 Task<IReadOnlyList<SupplySummary>> ListAsync(CancellationToken ct = default);
 Task<SupplySummary?> GetAsync(long id, CancellationToken ct = default);
 Task<IReadOnlyList<SupplyLine>> LinesAsync(long id, CancellationToken ct = default);
 Task<long> CreateAsync(long supplierId, string document, CancellationToken ct = default);
 Task AddLineAsync(long id, SupplyLineInput line, CancellationToken ct = default);
 Task PostAsync(long id, CancellationToken ct = default);
}
public interface ILookupRepository
{
 Task<IReadOnlyList<Lookup>> CategoriesAsync(CancellationToken ct = default);
 Task<IReadOnlyList<Lookup>> DepartmentsAsync(CancellationToken ct = default);
 Task<IReadOnlyList<Lookup>> CustomersAsync(CancellationToken ct = default);
 Task<IReadOnlyList<Lookup>> SuppliersAsync(CancellationToken ct = default);
 Task<IReadOnlyList<SupplierProduct>> SupplierProductsAsync(CancellationToken ct = default);
 Task<CurrentUser?> CurrentUserAsync(CancellationToken ct = default);
 Task<IReadOnlyList<ManagerProfile>> ManagersAsync(CancellationToken ct = default);
 Task RenameCurrentManagerAsync(string fullName, CancellationToken ct = default);
}
public interface IUnitOfWork : IAsyncDisposable
{
 IProductRepository Products { get; }
 IInventoryRepository Inventory { get; }
 ISaleRepository Sales { get; }
 IPaymentRepository Payments { get; }
 ISupplyRepository Supplies { get; }
 ILookupRepository Lookups { get; }
 Task BeginAsync(CancellationToken ct = default);
 Task CommitAsync(CancellationToken ct = default);
 Task RollbackAsync(CancellationToken ct = default);
}

