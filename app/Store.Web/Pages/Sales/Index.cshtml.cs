using Microsoft.AspNetCore.Mvc.RazorPages;
using Store.Web.Models;
using Store.Web.Services;
namespace Store.Web.Pages.Sales;
public sealed class IndexModel(IStoreService service) : PageModel
{
 public IReadOnlyList<SaleSummary> Sales { get; private set; }=[];
 public async Task OnGetAsync(CancellationToken ct) => Sales=await service.SalesAsync(ct);
}

