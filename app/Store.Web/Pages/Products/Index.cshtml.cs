using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Store.Web.Models;
using Store.Web.Services;
namespace Store.Web.Pages.Products;
public sealed class IndexModel(IStoreService service) : PageModel
{
 [BindProperty(SupportsGet=true)] public string? Search { get; set; }
 [BindProperty(SupportsGet=true)] public bool ShowArchive { get; set; }
 public IReadOnlyList<Product> Products { get; private set; } = [];
 public IReadOnlyList<ArchivedProduct> Archived { get; private set; } = [];
 public Task OnGetAsync(CancellationToken ct) => LoadAsync(ct);
 private async Task LoadAsync(CancellationToken ct)
 {
  if (ShowArchive) Archived = await service.ArchiveAsync(ct);
  else Products = await service.ProductsAsync(Search,ct);
 }
 public async Task<IActionResult> OnPostDeleteAsync(long id,CancellationToken ct)
 {
  try { await service.DeleteProductAsync(id,ct); TempData["Success"]="Товар переміщено до архіву. Історія документів збережена."; return RedirectToPage(new { Search }); }
  catch(StoreOperationException ex) { ModelState.AddModelError("",ex.Message); await LoadAsync(ct); return Page(); }
 }
 public async Task<IActionResult> OnPostRestoreAsync(long id,CancellationToken ct)
 {
  ShowArchive=true;
  try { await service.RestoreProductAsync(id,ct); TempData["Success"]="Товар відновлено в каталозі."; return RedirectToPage(new { ShowArchive=true }); }
  catch(StoreOperationException ex) { ModelState.AddModelError("",ex.Message); await LoadAsync(ct); return Page(); }
 }
}

