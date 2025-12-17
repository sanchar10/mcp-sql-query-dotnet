using Microsoft.Data.Sqlite;
using System.Data;

namespace CustomerQueryMcp.Data;

/// <summary>
/// SQLite database provider implementation.
/// </summary>
public class SqliteDatabaseProvider : IDatabaseProvider
{
    private readonly string _connectionString;

    public SqliteDatabaseProvider(string connectionString)
    {
        _connectionString = connectionString;
    }

    public string ProviderName => "SQLite";
    public string ConnectionString => _connectionString;
    public string ConcatOperator => "||";
    public string CurrentTimestamp => "datetime('now')";
    public string BooleanTrue => "1";
    public string BooleanFalse => "0";

    public IDbConnection CreateConnection() => new SqliteConnection(_connectionString);

    public string GetLimitSyntax(int limit) => $"LIMIT {limit}";

    public string WrapWithLimit(string selectQuery, int limit)
    {
        // For SQLite, we append LIMIT at the end
        return $"{selectQuery} LIMIT {limit}";
    }

    public string GetIdentitySyntax() => "INTEGER PRIMARY KEY AUTOINCREMENT";

    public string GetLastInsertIdSyntax() => "SELECT last_insert_rowid()";
}
