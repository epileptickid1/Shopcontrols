using Microsoft.AspNetCore.Antiforgery;

namespace Store.Web.Services;

/// <summary>Reject forms opened under a different manager, including forms in another tab.</summary>
public sealed class ManagerAntiforgeryData : IAntiforgeryAdditionalDataProvider
{
 public string GetAdditionalData(HttpContext context) => context.RequestServices.GetRequiredService<ManagerSession>().Username;
 public bool ValidateAdditionalData(HttpContext context, string additionalData) =>
  string.Equals(GetAdditionalData(context), additionalData, StringComparison.Ordinal);
}
