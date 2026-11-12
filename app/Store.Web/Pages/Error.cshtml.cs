using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace Store.Web.Pages;
[ResponseCache(Duration=0, Location=ResponseCacheLocation.None, NoStore=true)]
[IgnoreAntiforgeryToken]
public sealed class ErrorModel : PageModel
{
 public int? ErrorStatus { get; private set; }
 public void OnGet(int? statusCode) => ErrorStatus = statusCode;
 public void OnPost(int? statusCode) => ErrorStatus = statusCode;
}

