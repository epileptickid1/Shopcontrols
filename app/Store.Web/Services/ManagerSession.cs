using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Store.Web.Data;

namespace Store.Web.Services;

/// <summary>Local demo selection, not password authentication. Fixed for the lifetime of a request.</summary>
public sealed class ManagerSession
{
 public const string CookieName = "DepartmentStore.Manager";
 public const string ProtectionPurpose = "DepartmentStore.ManagerSelection.v1";
 private readonly HttpContext context;
 private readonly ManagerRegistry registry;
 private readonly IDataProtector protector;
 public string Username { get; }

 public ManagerSession(IHttpContextAccessor accessor, ManagerRegistry registry, IDataProtectionProvider protection)
 {
  context = accessor.HttpContext ?? throw new InvalidOperationException("Manager selection requires an HTTP request.");
  this.registry = registry;
  protector = protection.CreateProtector(ProtectionPurpose);
  Username = registry.DefaultUsername;
  if (context.Request.Cookies.TryGetValue(CookieName, out var cookie))
  {
   try
   {
    var selected = protector.Unprotect(cookie);
    if (registry.Contains(selected)) Username = selected;
   }
   catch (CryptographicException) { /* An invalid/old cookie cannot select a database login. */ }
  }
 }

 public async Task SwitchAsync(string username, CancellationToken ct = default)
 {
  if (!registry.Contains(username)) throw new StoreOperationException("Оберіть менеджера зі списку.");
  await using var work = new PostgreSqlUnitOfWork(registry.Source(username));
  var user = await work.Lookups.CurrentUserAsync(ct);
  if (user is null || user.Username != username || user.RoleName != "manager")
   throw new StoreOperationException("Цей менеджер неактивний. Оберіть іншого.");
  context.Response.Cookies.Append(CookieName, protector.Protect(username), new CookieOptions
  {
   HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Strict,
   IsEssential = true, Path = "/", MaxAge = TimeSpan.FromDays(30)
  });
  // The redirected request selects the new source; an in-flight transaction never changes actor.
 }
}
