using Microsoft.AspNetCore.Mvc.RazorPages;
using Store.Web.Models;
using Store.Web.Services;
namespace Store.Web.Pages.Supplies;
public sealed class IndexModel(IStoreService service) : PageModel
{
 public IReadOnlyList<SupplySummary> Supplies { get; private set; }=[];
 public async Task OnGetAsync(CancellationToken ct) => Supplies=await service.SuppliesAsync(ct);
}

