using Microsoft.Data.SqlClient;
using System.Data;

namespace CustomerQueryMcp.Data;

/// <summary>
/// SQL Server database provider implementation.
/// </summary>
public class SqlServerDatabaseProvider : IDatabaseProvider
{
    private readonly string _connectionString;

    public SqlServerDatabaseProvider(string connectionString)
    {
        _connectionString = connectionString;
    }

    public string ProviderName => "SqlServer";
    public string ConnectionString => _connectionString;
    public string ConcatOperator => "+";
    public string CurrentTimestamp => "GETUTCDATE()";
    public string BooleanTrue => "1";
    public string BooleanFalse => "0";

    public IDbConnection CreateConnection() => new SqlConnection(_connectionString);

    public string GetLimitSyntax(int limit) => $"TOP {limit}";

    public string WrapWithLimit(string selectQuery, int limit)
    {
        // For SQL Server, we insert TOP after SELECT
        // Handle SELECT DISTINCT case
        if (selectQuery.TrimStart().StartsWith("SELECT DISTINCT", StringComparison.OrdinalIgnoreCase))
        {
            return selectQuery.Replace("SELECT DISTINCT", $"SELECT DISTINCT TOP {limit}", StringComparison.OrdinalIgnoreCase);
        }
        return selectQuery.Replace("SELECT", $"SELECT TOP {limit}", StringComparison.OrdinalIgnoreCase);
    }

    public string GetIdentitySyntax() => "IDENTITY(1,1)";

    public string GetLastInsertIdSyntax() => "SELECT SCOPE_IDENTITY()";
}
