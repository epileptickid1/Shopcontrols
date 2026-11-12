using System.ComponentModel.DataAnnotations;
using Npgsql;
using Store.Web.Data;
using Store.Web.Models;

namespace Store.Web.Services;

/// <summary>Business operations coordinate several repositories in one database transaction.</summary>
public sealed class StoreService(IUnitOfWork work, ILogger<StoreService>? logger = null) : IStoreService
{
 public Task<IReadOnlyList<Product>> ProductsAsync(string? search = null, CancellationToken ct = default) => work.Products.ListAsync(search, ct);
 public Task<Product?> ProductAsync(long id, CancellationToken ct = default) => work.Products.GetAsync(id, ct);
 public Task<IReadOnlyList<ArchivedProduct>> ArchiveAsync(CancellationToken ct = default) => work.Products.ArchiveAsync(ct);
 public Task<IReadOnlyList<Stock>> InventoryAsync(long? departmentId = null, bool onlyLow = false, CancellationToken ct = default) => work.Inventory.ListAsync(departmentId, onlyLow, ct);
 public Task<IReadOnlyList<SaleSummary>> SalesAsync(CancellationToken ct = default) => work.Sales.ListAsync(ct);
 public Task<IReadOnlyList<SupplySummary>> SuppliesAsync(CancellationToken ct = default) => work.Supplies.ListAsync(ct);
 public Task<IReadOnlyList<Lookup>> CategoriesAsync(CancellationToken ct = default) => work.Lookups.CategoriesAsync(ct);
 public Task<IReadOnlyList<Lookup>> DepartmentsAsync(CancellationToken ct = default) => work.Lookups.DepartmentsAsync(ct);
 public Task<IReadOnlyList<Lookup>> CustomersAsync(CancellationToken ct = default) => work.Lookups.CustomersAsync(ct);
 public Task<IReadOnlyList<Lookup>> SuppliersAsync(CancellationToken ct = default) => work.Lookups.SuppliersAsync(ct);
 public Task<IReadOnlyList<SupplierProduct>> SupplierProductsAsync(CancellationToken ct = default) => work.Lookups.SupplierProductsAsync(ct);
 public Task<CurrentUser?> CurrentUserAsync(CancellationToken ct = default) => work.Lookups.CurrentUserAsync(ct);
 public Task<IReadOnlyList<ManagerProfile>> ManagersAsync(CancellationToken ct = default) => work.Lookups.ManagersAsync(ct);
 public async Task RenameCurrentManagerAsync(ManagerNameInput input, CancellationToken ct = default)
 {
  input.FullName = input.FullName?.Trim() ?? "";
  Validate(input);
  await TransactionAsync(async () => { await work.Lookups.RenameCurrentManagerAsync(input.FullName, ct); return true; }, ct);
 }

 public async Task<SaleDetails?> SaleAsync(long id, CancellationToken ct = default)
 {
  var sale = await work.Sales.GetAsync(id, ct);
  if (sale is null) return null;
  var lines = await work.Sales.LinesAsync(id, ct);
  var payments = await work.Payments.ListAsync(id, ct);
  return new(sale, lines, payments);
 }
 public async Task<SupplyDetails?> SupplyAsync(long id, CancellationToken ct = default)
 {
  var supply = await work.Supplies.GetAsync(id, ct);
  if (supply is null) return null;
  return new(supply, await work.Supplies.LinesAsync(id, ct));
 }
 public Task<long> SaveProductAsync(ProductInput input, CancellationToken ct = default)
 {
  input.Name = input.Name?.Trim() ?? "";
  input.Sku = input.Sku?.Trim() ?? "";
  input.Barcode = string.IsNullOrWhiteSpace(input.Barcode) ? null : input.Barcode.Trim();
  Validate(input);
  Precision(input.SalePrice, 2, "Ціна");
  return TransactionAsync(() => work.Products.SaveAsync(input, ct), ct);
 }
 public async Task DeleteProductAsync(long id, CancellationToken ct = default) =>
  await TransactionAsync(async () => { await work.Products.DeleteAsync(id, ct); return true; }, ct);
 public async Task RestoreProductAsync(long id, CancellationToken ct = default) =>
  await TransactionAsync(async () => { await work.Products.RestoreAsync(id, ct); return true; }, ct);
 public async Task SetSupplierProductAsync(SupplierProductInput input, CancellationToken ct = default)
 {
  Validate(input);
  Precision(input.PurchasePrice, 2, "Закупівельна ціна");
  await TransactionAsync(async () => { await work.Products.SetSupplierAsync(input, ct); return true; }, ct);
 }
 public async Task SetMinimumAsync(long departmentId, long productId, decimal minimum, CancellationToken ct = default)
 {
  if (departmentId <= 0 || productId <= 0 || minimum < 0) throw new StoreOperationException("Некоректні параметри залишку.");
  Precision(minimum, 3, "Мінімальний залишок");
  await TransactionAsync(async () => { await work.Inventory.SetMinimumAsync(departmentId, productId, minimum, ct); return true; }, ct);
 }
 public Task<long> CheckoutAsync(CheckoutInput input, CancellationToken ct = default)
 {
  input.ReceiptNumber = input.ReceiptNumber?.Trim() ?? "";
  Validate(input);
  if (input.CustomerId is <= 0) throw new StoreOperationException("Оберіть покупця зі списку або анонімну покупку.");
  if (input.Lines is null || input.Lines.Count is < 1 or > 100)
   throw new StoreOperationException("Продаж має містити від 1 до 100 рядків.");
  if (input.Payments is null || input.Payments.Count > 20)
   throw new StoreOperationException("Дозволено до 20 оплат на один чек.");
  if (input.Lines.GroupBy(x => (x.DepartmentId, x.ProductId)).Any(g => g.Count() > 1))
   throw new StoreOperationException("Один товар з одного відділу має бути в одному рядку. Об’єднайте кількості.");
  foreach (var line in input.Lines)
  {
   Validate(line); Precision(line.Quantity, 3, "Кількість"); Precision(line.DiscountAmount, 2, "Знижка");
  }
  foreach (var payment in input.Payments)
  {
   payment.Reference = string.IsNullOrWhiteSpace(payment.Reference) ? null : payment.Reference.Trim();
   Validate(payment); Precision(payment.Amount, 2, "Сума оплати");
  }
  // No transaction exists while the user is filling out the form.
  return TransactionAsync(async () =>
  {
   var id = await work.Sales.CreateAsync(input.ReceiptNumber, input.CustomerId, ct);
   foreach (var line in input.Lines.OrderBy(x => x.DepartmentId).ThenBy(x => x.ProductId))
    await work.Sales.AddLineAsync(id, line, ct);
   foreach (var payment in input.Payments) await work.Payments.AddAsync(id, payment, ct);
   await work.Sales.PostAsync(id, ct);
   return id;
  }, ct);
 }
 public Task<long> ReceiveAsync(SupplyInput input, CancellationToken ct = default)
 {
  input.DocumentNumber = input.DocumentNumber?.Trim() ?? "";
  Validate(input);
  if (input.Lines is null || input.Lines.Count is < 1 or > 100)
   throw new StoreOperationException("Постачання має містити від 1 до 100 рядків.");
  if (input.Lines.GroupBy(x => (x.DepartmentId, x.ProductId)).Any(g => g.Count() > 1))
   throw new StoreOperationException("Об’єднайте повторні рядки одного товару й відділу.");
  foreach (var line in input.Lines)
  {
   Validate(line); Precision(line.Quantity, 3, "Кількість");
   if (line.UnitCost.HasValue) Precision(line.UnitCost.Value, 2, "Закупівельна ціна");
  }
  return TransactionAsync(async () =>
  {
   var id = await work.Supplies.CreateAsync(input.SupplierId, input.DocumentNumber, ct);
   foreach (var line in input.Lines.OrderBy(x => x.DepartmentId).ThenBy(x => x.ProductId))
    await work.Supplies.AddLineAsync(id, line, ct);
   await work.Supplies.PostAsync(id, ct);
   return id;
  }, ct);
 }

 private async Task<T> TransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct)
 {
  var beganHere = false;
  try
  {
   await work.BeginAsync(ct);
   beganHere = true;
   var result = await operation();
   await work.CommitAsync(ct);
   return result;
  }
  catch (Exception error)
  {
   // Cancellation must roll back our transaction; a rejected nested call must not roll back its caller.
   if (beganHere)
   {
    try { await work.RollbackAsync(CancellationToken.None); }
    catch (Exception cleanupError) { logger?.LogWarning(cleanupError, "Could not finish transaction cleanup; preserving original failure."); }
   }
   if (error is PostgresException databaseError) throw Map(databaseError);
   if (error is NpgsqlException) throw new StoreOperationException("З’єднання з базою перервано. Перевірте історію за номером документа перед повторною спробою.", error);
   throw;
  }
 }
 private static void Validate(object input)
 {
  var issues = new List<ValidationResult>();
  if (!Validator.TryValidateObject(input, new ValidationContext(input), issues, true))
   throw new StoreOperationException(string.Join(" ", issues.Select(x => x.ErrorMessage)));
 }
 private static void Precision(decimal number, int digits, string field)
 {
  if (decimal.Round(number, digits) != number)
   throw new StoreOperationException($"{field}: дозволено не більше {digits} знаків після коми.");
 }
 private static StoreOperationException Map(PostgresException e)
 {
  var text = e.MessageText;
  string message = e.SqlState switch
  {
   "23505" => "Запис із таким артикулом, штрихкодом, номером документа або платіжним посиланням уже існує. Перевірте історію, якщо операцію надсилали повторно.",
   "23503" => "Пов’язаний запис не знайдено. Перевірте товар, відділ і довідники.",
   "42501" => "Недостатньо прав або обліковий запис неактивний.",
   "40001" or "40P01" => "Дані одночасно змінювала інша операція. Оновіть сторінку та спробуйте ще раз.",
   "22003" => "Вказане число перевищує допустимий розмір.",
   "23514" when text.Contains("Insufficient stock") => "Недостатньо товару у вибраному відділі. Продаж не збережено; залишки та оплати не змінено.",
   "23514" when text.Contains("Payment mismatch") => "Сума оплат має точно дорівнювати вартості товарів після знижок. Ціни могли змінитися — перевірте підсумок.",
   "23514" when text.Contains("integer") => "Для штучних товарів кількість має бути цілим числом.",
   "23514" when text.Contains("Supplier") || text.Contains("supplier") => "Постачальник не має активної пропозиції для цього товару.",
   "23514" when text.Contains("Cannot change unit") => "Одиницю виміру товару із залишками змінювати не можна.",
   "23514" when text.Contains("inactive") || text.Contains("not found") => "Запис не знайдено або він неактивний. Оновіть дані.",
   "23514" => "Операція порушує правила обліку. Перевірте кількість, знижку, суми та стан документа.",
   _ => "Операцію не виконано через помилку бази даних. Зміни відкочено."
  };
  return new(message, e);
 }
}


