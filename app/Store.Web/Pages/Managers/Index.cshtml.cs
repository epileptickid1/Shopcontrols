using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Store.Web.Models;
using Store.Web.Services;

namespace Store.Web.Pages.Managers;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class IndexModel(IStoreService service, ManagerSession session, ManagerRegistry registry) : PageModel
{
 public IReadOnlyList<ManagerProfile> Managers { get; private set; } = [];
 public ManagerProfile? CurrentManager { get; private set; }
 public string SelectedUsername => session.Username;
 public string FullName { get; private set; } = "";

 public async Task OnGetAsync(CancellationToken ct)
 {
  await LoadAsync(ct);
  FullName = CurrentManager?.FullName ?? "";
 }

 public async Task<IActionResult> OnPostSwitchAsync(string? username, CancellationToken ct)
 {
  try
  {
   await session.SwitchAsync(username ?? "", ct);
   TempData["Success"] = "Менеджера змінено. Оновіть інші відкриті вкладки перед збереженням змін.";
   return RedirectToPage();
  }
  catch (StoreOperationException ex) { ModelState.AddModelError("", ex.Message); }
  await OnGetAsync(ct);
  return Page();
 }

 public async Task<IActionResult> OnPostRenameAsync(string? fullName, CancellationToken ct)
 {
  FullName = fullName ?? "";
  try
  {
   await service.RenameCurrentManagerAsync(new ManagerNameInput { FullName = FullName }, ct);
   TempData["Success"] = "Ім’я менеджера збережено.";
   return RedirectToPage();
  }
  catch (StoreOperationException ex) { ModelState.AddModelError("", ex.Message); }
  await LoadAsync(ct);
  return Page();
 }

 private async Task LoadAsync(CancellationToken ct)
 {
  Managers = (await service.ManagersAsync(ct)).Where(m => registry.Contains(m.Username)).ToArray();
  CurrentManager = Managers.SingleOrDefault(m => m.Username == session.Username);
 }
}
