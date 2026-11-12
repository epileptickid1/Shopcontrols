using Microsoft.AspNetCore.Mvc.RazorPages;
using Store.Web.Models;
using Store.Web.Services;
namespace Store.Web.Pages;
public sealed class IndexModel(IStoreService service) : PageModel
{
 public int ProductCount { get; private set; }
 public int DepartmentCount { get; private set; }
 public IReadOnlyList<Stock> LowStock { get; private set; } = [];
 public IReadOnlyList<SaleSummary> RecentSales { get; private set; } = [];
 public CurrentUser? CurrentUser { get; private set; }
 public async Task OnGetAsync(CancellationToken ct)
 {
  CurrentUser = await service.CurrentUserAsync(ct);
  ProductCount = (await service.ProductsAsync(ct:ct)).Count;
  DepartmentCount = (await service.DepartmentsAsync(ct)).Count;
  LowStock = (await service.InventoryAsync(onlyLow:true,ct:ct));
  RecentSales = (await service.SalesAsync(ct)).Take(5).ToArray();
 }
}

