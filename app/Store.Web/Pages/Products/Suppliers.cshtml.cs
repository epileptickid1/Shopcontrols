using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Store.Web.Models;
using Store.Web.Services;

namespace Store.Web.Pages.Products;

public sealed class SuppliersModel(IStoreService service) : PageModel
{
 [BindProperty, Range(1, long.MaxValue, ErrorMessage = "Оберіть постачальника.")]
 public long SupplierId { get; set; }

 [BindProperty, Range(typeof(decimal), "0", "9999999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "Закупівельна ціна має бути від 0 до 9 999 999 999,99 грн.")]
 public decimal PurchasePrice { get; set; }

 public Product Product { get; private set; } = null!;
 public IReadOnlyList<Lookup> Suppliers { get; private set; } = [];
 public IReadOnlyList<SupplierProduct> Offers { get; private set; } = [];

 public async Task<IActionResult> OnGetAsync(long id, long? supplierId, CancellationToken ct)
 {
  if (!await LoadAsync(id, ct)) return NotFound();
  var offer = Offers.FirstOrDefault(x => x.SupplierId == supplierId);
  if (offer is not null) { SupplierId = offer.SupplierId; PurchasePrice = offer.PurchasePrice; }
  return Page();
 }

 public async Task<IActionResult> OnPostAsync(long id, CancellationToken ct)
 {
  if (!await LoadAsync(id, ct)) return NotFound();
  if (ModelState.IsValid)
  {
   try
   {
    await service.SetSupplierProductAsync(new SupplierProductInput { ProductId = id, SupplierId = SupplierId, PurchasePrice = PurchasePrice }, ct);
    TempData["Success"] = "Постачальника та закупівельну ціну збережено. Товар доступний для приймання постачань.";
    return RedirectToPage(new { id });
   }
   catch (StoreOperationException ex) { ModelState.AddModelError("", ex.Message); }
  }
  return Page();
 }

 private async Task<bool> LoadAsync(long id, CancellationToken ct)
 {
  var product = await service.ProductAsync(id, ct);
  if (product is null) return false;
  Product = product;
  Suppliers = await service.SuppliersAsync(ct);
  Offers = (await service.SupplierProductsAsync(ct)).Where(x => x.ProductId == id).OrderBy(x => x.SupplierName).ToList();
  return true;
 }
}

