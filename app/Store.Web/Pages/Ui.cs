using System.Globalization;
namespace Store.Web.Pages;
public static class Ui
{
 private static readonly CultureInfo Ua = CultureInfo.GetCultureInfo("uk-UA");
 private static readonly TimeZoneInfo Kyiv = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");
 public static string Money(decimal value) => value.ToString("N2", Ua);
 public static string Number(decimal value) => value.ToString("0.###", Ua);
 public static string Input(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
 public static string Date(DateTime value) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value, DateTimeKind.Utc), Kyiv).ToString("dd.MM.yyyy · HH:mm", Ua);
 public static string Unit(string unit) => unit switch { "pcs" => "шт.", "kg" => "кг", "l" => "л", "m" => "м", _ => unit };
 public static string Status(string status) => status switch { "posted" => "Проведено", "draft" => "Чернетка", "cancelled" => "Скасовано", _ => status };
 public static string Payment(string method) => method switch { "cash" => "Готівка", "card" => "Картка", "bank_transfer" => "Переказ", _ => method };
 public static string DocumentNumber(string prefix) => prefix + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
}

