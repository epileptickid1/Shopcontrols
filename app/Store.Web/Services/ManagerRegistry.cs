using Npgsql;

namespace Store.Web.Services;

/// <summary>Server-owned connections. Browser input can select only a configured login.</summary>
public sealed class ManagerRegistry : IAsyncDisposable
{
 private readonly Dictionary<string, NpgsqlDataSource> sources = new(StringComparer.Ordinal);
 public string DefaultUsername { get; }

 public ManagerRegistry(string primaryConnection, IEnumerable<string> additionalConnections)
 {
  var primary = new NpgsqlConnectionStringBuilder(primaryConnection);
  var settings = new[] { primary }.Concat(additionalConnections.Select(c => new NpgsqlConnectionStringBuilder(c))).ToArray();
  var usernames = new HashSet<string>(StringComparer.Ordinal);
  foreach (var item in settings)
  {
   if (string.IsNullOrWhiteSpace(item.Username) || item.Username is "postgres" or "store_owner" || !usernames.Add(item.Username))
    throw new InvalidOperationException("Кожен менеджер потребує окремого обмеженого логіна PostgreSQL.");
   if (item.Host != primary.Host || item.Port != primary.Port || item.Database != primary.Database)
    throw new InvalidOperationException("Усі менеджери мають працювати з тією самою базою даних.");
  }
  DefaultUsername = primary.Username!;
  foreach (var item in settings)
  {
   item.MaxPoolSize = 10;
   sources.Add(item.Username!, NpgsqlDataSource.Create(item.ConnectionString));
  }
 }

 public bool Contains(string username) => sources.ContainsKey(username);
 public NpgsqlDataSource Source(string username) => sources.TryGetValue(username, out var source)
  ? source : throw new StoreOperationException("Оберіть менеджера зі списку.");

 public async ValueTask DisposeAsync()
 {
  foreach (var source in sources.Values) await source.DisposeAsync();
 }
}
