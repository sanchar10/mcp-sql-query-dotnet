using System.Data;

namespace CustomerQueryMcp.Data;

/// <summary>
/// Abstraction for database provider to support both SQLite and SQL Server.
/// </summary>
public interface IDatabaseProvider
{
    /// <summary>
    /// Gets the database provider name (SqlServer or SQLite).
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Creates a new database connection.
    /// </summary>
    IDbConnection CreateConnection();

    /// <summary>
    /// Gets the connection string.
    /// </summary>
    string ConnectionString { get; }

    /// <summary>
    /// Gets the parameter prefix for SQL queries (@ for both SQLite and SQL Server).
    /// </summary>
    string ParameterPrefix => "@";

    /// <summary>
    /// Gets the SQL syntax for LIMIT clause.
    /// SQLite: LIMIT n
    /// SQL Server: TOP n (in SELECT) or OFFSET/FETCH
    /// </summary>
    string GetLimitSyntax(int limit);

    /// <summary>
    /// Gets the SQL syntax for string concatenation.
    /// SQLite: ||
    /// SQL Server: +
    /// </summary>
    string ConcatOperator { get; }

    /// <summary>
    /// Gets the SQL syntax for current timestamp.
    /// </summary>
    string CurrentTimestamp { get; }

    /// <summary>
    /// Gets the SQL syntax for boolean true value.
    /// </summary>
    string BooleanTrue { get; }

    /// <summary>
    /// Gets the SQL syntax for boolean false value.
    /// </summary>
    string BooleanFalse { get; }

    /// <summary>
    /// Wraps a SELECT query with a row limit.
    /// </summary>
    string WrapWithLimit(string selectQuery, int limit);

    /// <summary>
    /// Gets the identity column syntax for auto-increment.
    /// </summary>
    string GetIdentitySyntax();

    /// <summary>
    /// Gets the syntax for getting the last inserted ID.
    /// </summary>
    string GetLastInsertIdSyntax();
}
