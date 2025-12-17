namespace CustomerQueryMcp.Data;

/// <summary>
/// Factory for creating database providers based on configuration.
/// </summary>
public static class DatabaseProviderFactory
{
    /// <summary>
    /// Creates a database provider based on the provider name.
    /// </summary>
    /// <param name="providerName">The provider name (SqlServer or SQLite).</param>
    /// <param name="connectionString">The connection string for the database.</param>
    /// <returns>An instance of IDatabaseProvider.</returns>
    public static IDatabaseProvider Create(string providerName, string connectionString)
    {
        return providerName.ToLowerInvariant() switch
        {
            "sqlserver" or "mssql" => new SqlServerDatabaseProvider(connectionString),
            "sqlite" => new SqliteDatabaseProvider(connectionString),
            _ => throw new ArgumentException($"Unknown database provider: {providerName}. Supported providers: SqlServer, SQLite")
        };
    }
}
