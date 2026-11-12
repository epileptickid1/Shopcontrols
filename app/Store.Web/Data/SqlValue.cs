using Npgsql;
using NpgsqlTypes;

namespace Store.Web.Data;

internal static class SqlValue
{
    public static NpgsqlParameter Id(long? value) => Parameter(NpgsqlDbType.Bigint, value);
    public static NpgsqlParameter Text(string? value) => Parameter(NpgsqlDbType.Text, value);
    public static NpgsqlParameter Number(decimal? value) => Parameter(NpgsqlDbType.Numeric, value);
    public static NpgsqlParameter Flag(bool value) => Parameter(NpgsqlDbType.Boolean, value);

    private static NpgsqlParameter Parameter(NpgsqlDbType type, object? value) =>
        new() { NpgsqlDbType = type, Value = value ?? DBNull.Value };

    public static string? NullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    public static long? NullableId(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
}

