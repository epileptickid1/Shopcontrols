using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Antiforgery;
using Store.Web.Data;
using Store.Web.Services;

var builder = WebApplication.CreateBuilder(args);
var directory = new DirectoryInfo(builder.Environment.ContentRootPath);
while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "task.txt"))) directory = directory.Parent;
var workspace = directory?.FullName ?? builder.Environment.ContentRootPath;
builder.Configuration.AddJsonFile(Path.Combine(workspace, ".runtime", "app-connection.json"), optional: true, reloadOnChange: false);
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);
var connectionString = builder.Configuration.GetConnectionString("Store");
if (string.IsNullOrWhiteSpace(connectionString))
 throw new InvalidOperationException("Спочатку виконайте scripts/setup-app.ps1. Підключення Store не налаштоване.");

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("uk-UA");
builder.Services.AddRazorPages();
builder.Services.AddDataProtection()
 .SetApplicationName("DepartmentStore.Local")
 .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(workspace, ".runtime", "data-protection")));
builder.Services.AddSingleton(_ => new ManagerRegistry(connectionString,
 builder.Configuration.GetSection("ManagerConnections").Get<string[]>() ?? []));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ManagerSession>();
builder.Services.AddScoped<IUnitOfWork>(services => new PostgreSqlUnitOfWork(
 services.GetRequiredService<ManagerRegistry>().Source(services.GetRequiredService<ManagerSession>().Username)));
builder.Services.AddScoped<IStoreService, StoreService>();
builder.Services.AddSingleton<IAntiforgeryAdditionalDataProvider, ManagerAntiforgeryData>();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.ValueCountLimit = 2500);
builder.Services.Configure<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>(o =>
{
 o.Cookie.Name = "DepartmentStore.Antiforgery";
 o.Cookie.SameSite = SameSiteMode.Strict;
});
var app = builder.Build();
app.UseExceptionHandler("/Error");
app.UseStatusCodePagesWithReExecute("/Error", "?statusCode={0}");
app.Use(async (context, next) =>
{
 context.Response.Headers["X-Content-Type-Options"] = "nosniff";
 context.Response.Headers["X-Frame-Options"] = "DENY";
 context.Response.Headers["Referrer-Policy"] = "same-origin";
 await next();
});
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.Run();
public partial class Program;

