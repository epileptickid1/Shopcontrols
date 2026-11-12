using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Store.Web.Models;
using Store.Web.Services;
namespace Store.Web.Pages.Products;
public sealed class EditModel(IStoreService service) : PageModel
{
 [BindProperty] public ProductInput Input { get; set; } = new();
 public IReadOnlyList<Lookup> Categories { get; private set; } = [];
 public Product? Existing { get; private set; }
 public async Task<IActionResult> OnGetAsync(long? id,CancellationToken ct)
 {
  Categories=await service.CategoriesAsync(ct);
  if(id is null) return Page();
  Existing=await service.ProductAsync(id.Value,ct);
  if(Existing is null) return NotFound();
  Input=new ProductInput { Id=Existing.Id,CategoryId=Existing.CategoryId,Sku=Existing.Sku,Barcode=Existing.Barcode,Name=Existing.Name,Unit=Existing.Unit,SalePrice=Existing.SalePrice };
  return Page();
 }
 public async Task<IActionResult> OnPostAsync(long? id,CancellationToken ct)
 {
  if(id!=Input.Id) return BadRequest();
  if(ModelState.IsValid) {
   try {
    var savedId=await service.SaveProductAsync(Input,ct);
    if(Input.Id is null) {
     TempData["Success"]="Товар додано. Вкажіть постачальника, щоб приймати його постачання.";
     return RedirectToPage("/Products/Suppliers",new {id=savedId});
    }
    TempData["Success"]="Зміни товару збережено.";
    return RedirectToPage("/Products/Index");
   }
   catch(StoreOperationException ex) { ModelState.AddModelError("",ex.Message); }
  }
  Categories=await service.CategoriesAsync(ct);
  if(Input.Id is long existingId) Existing=await service.ProductAsync(existingId,ct);
  return Page();
 }
}
