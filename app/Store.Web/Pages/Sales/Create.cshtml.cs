using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Store.Web.Models;
using Store.Web.Services;
namespace Store.Web.Pages.Sales;
public sealed class CreateModel(IStoreService service) : PageModel
{
 [BindProperty] public CheckoutInput Input { get; set; }=new();
 public IReadOnlyList<Product> Products { get; private set; }=[];
 public IReadOnlyList<Stock> Stock { get; private set; }=[];
 public IReadOnlyList<Lookup> Departments { get; private set; }=[];
 public IReadOnlyList<Lookup> Customers { get; private set; }=[];
 public async Task OnGetAsync(CancellationToken ct)
 { Input.ReceiptNumber=Ui.DocumentNumber("CHK"); Input.Lines.Add(new()); Input.Payments.Add(new()); await LoadAsync(ct); }
 private async Task LoadAsync(CancellationToken ct)
 { Products=await service.ProductsAsync(ct:ct); Stock=await service.InventoryAsync(ct:ct); Departments=await service.DepartmentsAsync(ct); Customers=await service.CustomersAsync(ct); }
 public async Task<IActionResult> OnPostAsync(CancellationToken ct)
 {
  Input.Lines ??=[]; Input.Payments ??=[];
  if(ModelState.IsValid) {
   try { var id=await service.CheckoutAsync(Input,ct); TempData["Success"]="Продаж оформлено. Оплати збережено, залишки оновлено."; return RedirectToPage("/Sales/Details",new {id}); }
   catch(StoreOperationException ex) { ModelState.AddModelError("",ex.Message); }
  }
  await LoadAsync(ct); return Page();
 }
}

