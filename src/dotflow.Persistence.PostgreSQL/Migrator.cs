using Npgsql;

namespace Dotflow.Persistence.PostgreSQL;

public static class Migrator
{
    public static async Task MigrateAsync(string connectionString, CancellationToken ct = default)
    {
        var sql = await ReadSchemaSqlAsync();
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string> ReadSchemaSqlAsync()
    {
        var assembly = typeof(Migrator).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("Schema.sql"))
            ?? throw new InvalidOperationException("Schema.sql embedded resource not found.");

        await using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
