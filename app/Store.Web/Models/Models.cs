using System.ComponentModel.DataAnnotations;

namespace Store.Web.Models;

public sealed record Product(long Id, long CategoryId, string CategoryName, string Sku, string? Barcode,
 string Name, string Unit, decimal SalePrice, DateTime UpdatedAt, long UpdatedBy);
public sealed record ArchivedProduct(long Id, string Sku, string Name, DateTime DeletedAt, DateTime UpdatedAt, long UpdatedBy);
public sealed record Lookup(long Id, string Name);
public sealed record Stock(long DepartmentId, string DepartmentName, long ProductId, string ProductName,
 string Sku, string Unit, decimal Quantity, decimal MinimumQuantity, bool NeedsRestock);
public sealed record SaleSummary(long Id, string ReceiptNumber, long? CustomerId, string? CustomerName,
 string Cashier, DateTime SoldAt, string Status, decimal Total, decimal Paid, decimal Balance);
public sealed record SaleLine(long Id, long DepartmentId, long ProductId, string DepartmentName,
 string ProductName, string Sku, string Unit, decimal Quantity, decimal UnitPrice, decimal DiscountAmount, decimal LineTotal);
public sealed record PaymentInfo(long Id, string Method, decimal Amount, DateTime PaidAt, string? Reference);
public sealed record SaleDetails(SaleSummary Sale, IReadOnlyList<SaleLine> Lines, IReadOnlyList<PaymentInfo> Payments);
public sealed record SupplySummary(long Id, long SupplierId, string SupplierName, string DocumentNumber,
 DateTime ReceivedAt, string Status, decimal Total);
public sealed record SupplyLine(long Id, long DepartmentId, long ProductId, string DepartmentName,
 string ProductName, decimal Quantity, decimal UnitCost, decimal LineTotal);
public sealed record SupplyDetails(SupplySummary Supply, IReadOnlyList<SupplyLine> Lines);
public sealed record SupplierProduct(long ProductId, long SupplierId, string ProductName, string SupplierName, decimal PurchasePrice);
public sealed record CurrentUser(long Id, string Username, string? FullName, string RoleName);
public sealed record ManagerProfile(long Id, string Username, string FullName, string RoleName, DateTime UpdatedAt, long UpdatedBy);

public sealed class ManagerNameInput
{
 [Required(ErrorMessage = "Вкажіть ім’я менеджера."), StringLength(200, ErrorMessage = "Ім’я має містити до 200 символів.")]
 public string FullName { get; set; } = "";
}

public sealed class ProductInput
{
 public long? Id { get; set; }
 [Range(1, long.MaxValue, ErrorMessage = "Оберіть категорію.")] public long CategoryId { get; set; }
 [Required(ErrorMessage = "Вкажіть артикул."), StringLength(50)] public string Sku { get; set; } = "";
 [StringLength(50)] public string? Barcode { get; set; }
 [Required(ErrorMessage = "Вкажіть назву."), StringLength(200)] public string Name { get; set; } = "";
 [Required, RegularExpression("^(pcs|kg|l|m)$", ErrorMessage = "Некоректна одиниця виміру.")] public string Unit { get; set; } = "pcs";
 [Range(typeof(decimal), "0", "9999999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "Ціна має бути невід’ємною.")] public decimal SalePrice { get; set; }
}
public sealed class SupplierProductInput
{
 [Range(1, long.MaxValue)] public long ProductId { get; set; }
 [Range(1, long.MaxValue, ErrorMessage = "Оберіть постачальника.")] public long SupplierId { get; set; }
 [Range(typeof(decimal), "0", "9999999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "Закупівельна ціна має бути невід’ємною.")] public decimal PurchasePrice { get; set; }
}
public sealed class SaleLineInput
{
 [Range(1, long.MaxValue)] public long DepartmentId { get; set; }
 [Range(1, long.MaxValue)] public long ProductId { get; set; }
 [Range(typeof(decimal), "0.001", "99999999999.999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "Вкажіть додатну кількість.")] public decimal Quantity { get; set; } = 1;
 [Range(typeof(decimal), "0", "999999999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal DiscountAmount { get; set; }
}
public sealed class PaymentInput
{
 [Required, RegularExpression("^(cash|card|bank_transfer)$")] public string Method { get; set; } = "cash";
 [Range(typeof(decimal), "0.01", "999999999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "Сума оплати має бути додатною.")] public decimal Amount { get; set; }
 [StringLength(150)] public string? Reference { get; set; }
}
public sealed class CheckoutInput
{
 [Required, StringLength(50)] public string ReceiptNumber { get; set; } = "";
 public long? CustomerId { get; set; }
 [MinLength(1, ErrorMessage = "Додайте принаймні один товар.")] public List<SaleLineInput> Lines { get; set; } = [];
 public List<PaymentInput> Payments { get; set; } = [];
}
public sealed class SupplyLineInput
{
 [Range(1, long.MaxValue)] public long DepartmentId { get; set; }
 [Range(1, long.MaxValue)] public long ProductId { get; set; }
 [Range(typeof(decimal), "0.001", "99999999999.999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal Quantity { get; set; } = 1;
 [Range(typeof(decimal), "0", "9999999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal? UnitCost { get; set; }
}
public sealed class SupplyInput
{
 [Range(1, long.MaxValue, ErrorMessage = "Оберіть постачальника.")] public long SupplierId { get; set; }
 [Required(ErrorMessage = "Вкажіть номер документа."), StringLength(100)] public string DocumentNumber { get; set; } = "";
 [MinLength(1, ErrorMessage = "Додайте принаймні один товар.")] public List<SupplyLineInput> Lines { get; set; } = [];
}


