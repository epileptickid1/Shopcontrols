using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Store.Web.Models;
using Store.Web.Services;
namespace Store.Web.Pages.Supplies;
public sealed class CreateModel(IStoreService service) : PageModel
{
 [BindProperty] public SupplyInput Input { get; set; }=new();
 public IReadOnlyList<Lookup> Suppliers { get; private set; }=[];
 public IReadOnlyList<Lookup> Departments { get; private set; }=[];
 public IReadOnlyList<SupplierProduct> SupplierProducts { get; private set; }=[];
 public IReadOnlyList<Product> Products { get; private set; }=[];
 public async Task OnGetAsync(CancellationToken ct)
 { Input.DocumentNumber=Ui.DocumentNumber("SUP"); Input.Lines.Add(new()); await LoadAsync(ct); }
 private async Task LoadAsync(CancellationToken ct)
 { Suppliers=await service.SuppliersAsync(ct); Departments=await service.DepartmentsAsync(ct); SupplierProducts=await service.SupplierProductsAsync(ct); Products=await service.ProductsAsync(ct:ct); }
 public async Task<IActionResult> OnPostAsync(CancellationToken ct)
 {
  Input.Lines ??=[];
  if(ModelState.IsValid) {
   try { var id=await service.ReceiveAsync(Input,ct); TempData["Success"]="Постачання прийнято. Товари зараховано до залишків."; return RedirectToPage("/Supplies/Details",new {id}); }
   catch(StoreOperationException ex) { ModelState.AddModelError("",ex.Message); }
  }
  await LoadAsync(ct); return Page();
 }
}

