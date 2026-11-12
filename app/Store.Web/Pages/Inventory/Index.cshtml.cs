using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Store.Web.Models;
using Store.Web.Services;
namespace Store.Web.Pages.Inventory;
public sealed class IndexModel(IStoreService service) : PageModel
{
 [BindProperty(SupportsGet=true)] public long? DepartmentId { get; set; }
 [BindProperty(SupportsGet=true)] public bool OnlyLow { get; set; }
 public IReadOnlyList<Stock> Stock { get; private set; }=[];
 public IReadOnlyList<Lookup> Departments { get; private set; }=[];
 public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);
 private async Task LoadAsync(CancellationToken ct) { Departments=await service.DepartmentsAsync(ct); Stock=await service.InventoryAsync(DepartmentId,OnlyLow,ct); }
 public async Task<IActionResult> OnPostMinimumAsync(long productId,long stockDepartmentId,decimal minimum,CancellationToken ct)
 {
  if(minimum<0) ModelState.AddModelError("","Мінімальний залишок має бути невід’ємним.");
  if(ModelState.IsValid) {
   try { await service.SetMinimumAsync(stockDepartmentId,productId,minimum,ct); TempData["Success"]="Мінімальний залишок оновлено."; return RedirectToPage(new { DepartmentId,OnlyLow }); }
   catch(StoreOperationException ex) { ModelState.AddModelError("",ex.Message); }
  }
  await LoadAsync(ct); return Page();
 }
}

