using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Store.Web.Data;
using Store.Web.Models;
using Store.Web.Services;

static class ManagerSessionChecks
{
 public static async Task<int> RunAsync()
 {
  const string primary = "Host=localhost;Database=store_webtests;Username=manager_a";
  const string second = "Host=localhost;Database=store_webtests;Username=manager_b";
  await using var registry = new ManagerRegistry(primary, [second]);
  var protection = new EphemeralDataProtectionProvider();
  var protector = protection.CreateProtector(ManagerSession.ProtectionPurpose);
  var services = new ServiceCollection();
  services.AddLogging();
  services.AddSingleton<IDataProtectionProvider>(protection);
  services.AddSingleton<IAntiforgeryAdditionalDataProvider, ManagerAntiforgeryData>();
  services.AddAntiforgery(o => { o.Cookie.Name = "Test.Antiforgery"; o.HeaderName = "X-Test-CSRF"; });
  await using var provider = services.BuildServiceProvider();
  var antiforgery = provider.GetRequiredService<IAntiforgery>();
  var passed = 0;

  void Check(bool condition, string name)
  {
   if (!condition) throw new InvalidOperationException(name);
   passed++;
   Console.WriteLine("PASS " + name);
  }

  (DefaultHttpContext Context, ManagerSession Session, ServiceProvider Services) Request(string? cookie = null)
  {
   var context = new DefaultHttpContext();
   context.Request.Scheme = "http";
   context.Request.Host = new HostString("localhost");
   if (cookie is not null) context.Request.Headers.Cookie = ManagerSession.CookieName + "=" + cookie;
   var session = new ManagerSession(new HttpContextAccessor { HttpContext = context }, registry, protection);
   var scopedServices = new ServiceCollection().AddSingleton(session).BuildServiceProvider();
   context.RequestServices = scopedServices;
   return (context, session, scopedServices);
  }

  try
  {
   var a = Request();
   using var aServices = a.Services;
   var b = Request(protector.Protect("manager_b"));
   using var bServices = b.Services;
   Check(a.Session.Username == "manager_a" && b.Session.Username == "manager_b", "Separate browsers retain separate manager selections");
   a.Context.Request.Headers.Cookie = ManagerSession.CookieName + "=" + protector.Protect("manager_b");
   Check(a.Session.Username == "manager_a", "Request actor remains fixed after request creation");
   foreach (var cookie in new[] { "tampered-cookie", protector.Protect("postgres"), protector.Protect("unknown_manager") })
   {
    var invalid = Request(cookie);
    using var invalidServices = invalid.Services;
    Check(invalid.Session.Username == "manager_a", "Invalid or unconfigured selection cannot choose a database login");
   }
   try { await a.Session.SwitchAsync("postgres"); throw new Exception("Unexpected switch to postgres"); }
   catch (StoreOperationException) { Check(!a.Context.Response.Headers.ContainsKey("Set-Cookie"), "Unconfigured manager switch is rejected before connecting"); }

   var tokens = antiforgery.GetAndStoreTokens(a.Context);
   var csrfCookie = a.Context.Response.Headers.SetCookie.Single(h => h!.StartsWith("Test.Antiforgery="))!.Split(';')[0];
   async Task<bool> PostValid(string? managerCookie, string? requestToken)
   {
    var post = Request(managerCookie);
    using var postServices = post.Services;
    post.Context.Request.Method = "POST";
    post.Context.Request.Headers.Cookie = string.Join("; ", new[] { post.Context.Request.Headers.Cookie.ToString(), csrfCookie }.Where(x => x.Length > 0));
    if (requestToken is not null) post.Context.Request.Headers["X-Test-CSRF"] = requestToken;
    return await antiforgery.IsRequestValidAsync(post.Context);
   }
   Check(await PostValid(null, tokens.RequestToken), "Real antiforgery token accepts the manager who opened the form");
   Check(!await PostValid(protector.Protect("manager_b"), tokens.RequestToken), "Real antiforgery validation rejects a stale form after switching managers");
   Check(!await PostValid(null, null), "POST without an antiforgery token is rejected");

   await using var work = new PostgreSqlUnitOfWork(registry.Source("manager_a"));
   var service = new StoreService(work);
   foreach (var name in new[] { "", " \t\n", new string('x', 201) })
   {
    try { await service.RenameCurrentManagerAsync(new ManagerNameInput { FullName = name }); throw new Exception("Invalid name accepted"); }
    catch (StoreOperationException) { Check(true, "Invalid manager name is rejected without database writes"); }
   }
   await ManagerPageChecks.RunAsync(registry, protection);
   Check(true, "Compiled manager page renders selected profile, encoded names, handler URLs and protected forms");
   Console.WriteLine($"{passed} offline manager session checks passed.");
   return 0;
  }
  catch (Exception ex)
  {
   Console.Error.WriteLine("FAIL manager session checks: " + ex);
   return 1;
  }
 }
}
