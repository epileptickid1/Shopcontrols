using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Store.Web.Models;
using Store.Web.Services;
namespace Store.Web.Pages.Sales;
public sealed class DetailsModel(IStoreService service) : PageModel
{
 public SaleDetails Details { get; private set; }=null!;
 public async Task<IActionResult> OnGetAsync(long id,CancellationToken ct)
 { var details=await service.SaleAsync(id,ct); if(details is null)return NotFound(); Details=details; return Page(); }
}

