using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Store.Web.Models;
using Store.Web.Services;

static class ManagerPageChecks
{
 // Render the compiled Razor page in memory; this makes no HTTP or database connections.
 public static async Task RunAsync(ManagerRegistry registry, IDataProtectionProvider protection)
 {
  DirectoryInfo? root = new(Environment.CurrentDirectory);
  while (root is not null && !File.Exists(Path.Combine(root.FullName, "task.txt"))) root = root.Parent;
  if (root is null) throw new InvalidOperationException("Run offline checks from the project directory.");
  var builder = WebApplication.CreateBuilder(new WebApplicationOptions
  {
   ApplicationName = typeof(StoreService).Assembly.GetName().Name,
   ContentRootPath = Path.Combine(root.FullName, "app", "Store.Web")
  });
  builder.Logging.ClearProviders();
  builder.Services.AddRazorPages();
  builder.Services.AddSingleton(registry);
  builder.Services.AddSingleton(protection);
  builder.Services.AddHttpContextAccessor();
  builder.Services.AddScoped<ManagerSession>();
  builder.Services.AddSingleton<IAntiforgeryAdditionalDataProvider, ManagerAntiforgeryData>();
  builder.Services.AddSingleton(DispatchProxy.Create<IStoreService, ManagerPageStoreProxy>());
  WebApplication? mappedApp = null;
  builder.Services.AddSingleton<EndpointDataSource>(_ => new CompositeEndpointDataSource(((IEndpointRouteBuilder)mappedApp!).DataSources));
  await using var app = builder.Build();
  mappedApp = app;
  app.MapRazorPages();
  await using var scope = app.Services.CreateAsyncScope();
  var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
  context.Request.Scheme = "http";
  context.Request.Host = new HostString("localhost");
  context.Request.Path = "/Managers";
  scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
  var model = new Store.Web.Pages.Managers.IndexModel(scope.ServiceProvider.GetRequiredService<IStoreService>(),
   scope.ServiceProvider.GetRequiredService<ManagerSession>(), registry);
  await model.OnGetAsync(CancellationToken.None);
  var route = new RouteData();
  route.Values["page"] = "/Managers/Index";
  context.Request.RouteValues = route.Values;
  context.SetEndpoint(((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints)
   .First(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.RazorPages.PageActionDescriptor>()?.ViewEnginePath == "/Managers/Index"));
  var action = new ActionContext(context, route, new ActionDescriptor());
  var view = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>().GetView(null, "/Pages/Managers/Index.cshtml", true);
  if (!view.Success) throw new InvalidOperationException("Compiled manager page missing.");
  using var writer = new StringWriter();
  var data = new ViewDataDictionary<Store.Web.Pages.Managers.IndexModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
  if (view.View is RazorView razor && razor.RazorPage is Microsoft.AspNetCore.Mvc.RazorPages.Page page)
  {
   page.PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext(action) { ViewData = data };
   model.PageContext = page.PageContext;
  }
  var tempData = new TempDataDictionary(context, scope.ServiceProvider.GetRequiredService<ITempDataProvider>());
  await view.View.RenderAsync(new ViewContext(action, view.View, data, tempData, writer, new HtmlHelperOptions()));
  var html = writer.ToString();
  if (Regex.Matches(html, "<option[^>]* selected=").Count != 1 || !html.Contains("value=\"manager_a\" selected="))
   throw new InvalidOperationException("Manager page selected the wrong option.");
  if (Regex.Matches(html, "name=\"__RequestVerificationToken\"").Count != 2)
   throw new InvalidOperationException("Both manager forms need antiforgery tokens.");
  foreach (var handler in new[] { "Switch", "Rename" })
   if (!Regex.IsMatch(html, "action=\"/Managers(?:/Index)?\\?handler=" + handler + "\""))
    throw new InvalidOperationException("Manager form has no working handler URL: " + handler + "; " + string.Join("; ", Regex.Matches(html, "<form[^>]*>").Select(m => m.Value)));
  if (html.Contains("<script>alert") || !html.Contains("&lt;script&gt;"))
   throw new InvalidOperationException("Manager names must be HTML encoded.");
 }
}

public class ManagerPageStoreProxy : DispatchProxy
{
 protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
 {
  nameof(IStoreService.CurrentUserAsync) => Task.FromResult<CurrentUser?>(new(2, "manager_a", "<script>alert(1)</script>", "manager")),
  nameof(IStoreService.ManagersAsync) => Task.FromResult<IReadOnlyList<ManagerProfile>>([
   new(2, "manager_a", "<script>alert(1)</script>", "manager", DateTime.UtcNow, 2),
   new(4, "manager_b", "Другий менеджер", "manager", DateTime.UtcNow, 4)
  ]),
  _ => throw new NotSupportedException("Unexpected call while rendering manager page: " + targetMethod?.Name)
 };
}
