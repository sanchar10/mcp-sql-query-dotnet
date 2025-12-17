using CustomerQueryMcp.Data;
using CustomerQueryMcp.Models;
using CustomerQueryMcp.Models.Dtos;
using Dapper;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace CustomerQueryMcp.Services;

/// <summary>
/// Schema-driven query builder for domain queries.
/// Uses JOINs for efficient single-query execution with overall record limits.
/// Supports nested entity relationships with explicit parent specification.
/// Supports both SQLite and SQL Server databases.
/// Fully generic - no per-entity code needed!
/// </summary>
public class DomainQueryBuilder : IDomainQueryBuilder
{
    // OpenTelemetry ActivitySource for custom tracing
    private static readonly ActivitySource ActivitySource = new("CustomerQueryMcp", "2.0.0");

    private readonly IDatabaseProvider _dbProvider;
    private readonly EntitySchemaConfig _schemaConfig;
    private readonly ILogger<DomainQueryBuilder> _logger;

    // Query state
    private string? _primaryEntity;
    private readonly List<RelatedEntityConfig> _relatedEntities = new();
    private EntityFilter? _primaryFilter;
    private int? _overallLimit;
    private readonly HashSet<string> _selectedEntities = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _selectedFields = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Configuration for a related entity in the query.
    /// </summary>
    private class RelatedEntityConfig
    {
        public string EntityName { get; set; } = "";
        public EntityFilter? Filter { get; set; }
        public string? ParentEntity { get; set; }
    }

    public DomainQueryBuilder(
        IDatabaseProvider dbProvider,
        EntitySchemaConfig schemaConfig,
        ILogger<DomainQueryBuilder> logger)
    {
        _dbProvider = dbProvider;
        _schemaConfig = schemaConfig;
        _logger = logger;
    }

    /// <summary>
    /// Creates a fresh builder instance for a new query.
    /// </summary>
    public IDomainQueryBuilder Create()
    {
        return new DomainQueryBuilder(_dbProvider, _schemaConfig, _logger);
    }

    public IDomainQueryBuilder From(string entityName)
    {
        _primaryEntity = entityName;
        return this;
    }

    public IDomainQueryBuilder WithRelated(string entityName, EntityFilter? filter = null, string? parent = null)
    {
        _relatedEntities.Add(new RelatedEntityConfig
        {
            EntityName = entityName,
            Filter = filter,
            ParentEntity = parent ?? _primaryEntity // Default to primary entity
        });
        return this;
    }

    public IDomainQueryBuilder Select(params string[] entityNames)
    {
        _selectedEntities.Clear();
        foreach (var name in entityNames)
        {
            _selectedEntities.Add(name);
        }
        return this;
    }

    public IDomainQueryBuilder SelectFields(string entityName, params string[] fields)
    {
        if (!_selectedFields.TryGetValue(entityName, out var fieldSet))
        {
            fieldSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _selectedFields[entityName] = fieldSet;
        }
        foreach (var field in fields)
        {
            fieldSet.Add(field);
        }
        return this;
    }

    public IDomainQueryBuilder Where(EntityFilter filter)
    {
        _primaryFilter = filter;
        return this;
    }

    public IDomainQueryBuilder Limit(int? limit)
    {
        _overallLimit = limit;
        return this;
    }

    public async Task<DomainQueryResult> ExecuteAsync(CancellationToken ct = default)
    {
        // Start OpenTelemetry trace span for the entire query execution
        using var activity = ActivitySource.StartActivity("DomainQuery.Execute.Join", ActivityKind.Internal);
        activity?.SetTag("db.system", _dbProvider.ProviderName.ToLowerInvariant());
        activity?.SetTag("query.primary_entity", _primaryEntity);
        activity?.SetTag("query.related_count", _relatedEntities.Count);
        activity?.SetTag("query.mode", "join");

        try
        {
            // Validate required fields
            if (string.IsNullOrEmpty(_primaryEntity))
                return DomainQueryResult.Failed("Primary entity not specified. Call From() first.");

            if (_primaryFilter == null || _primaryFilter.Conditions.Count == 0)
                return DomainQueryResult.Failed("Filter not specified. Call Where() first.");

            var primarySchema = GetEntitySchema(_primaryEntity);
            if (primarySchema == null)
                return DomainQueryResult.Failed($"Unknown entity: {_primaryEntity}");

            var result = new DomainQueryResult();

            using var connection = CreateConnection();

            // Build and execute JOIN-based query
            var (sql, parameters) = BuildJoinQuery(primarySchema);

            _logger.LogDebug("Executing JOIN query: {Sql}", sql);

            IEnumerable<dynamic> queryResults;
            using (var queryActivity = ActivitySource.StartActivity("SQL.Query.Join", ActivityKind.Client))
            {
                queryActivity?.SetTag("db.statement", sql);
                queryActivity?.SetTag("db.join_count", _relatedEntities.Count);

                queryResults = await connection.QueryAsync(
                    new CommandDefinition(sql, parameters, cancellationToken: ct));
            }

            // Process joined results into separate entity collections
            ProcessJoinResults(queryResults, primarySchema, result);

            // Apply Select() filter if specified
            ApplySelect(result);

            activity?.SetTag("query.success", true);
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetTag("query.success", false);
            activity?.SetTag("error.message", ex.Message);
            _logger.LogError(ex, "Error executing domain query");
            return DomainQueryResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Builds a JOIN-based query for all entities.
    /// </summary>
    private (string Sql, DynamicParameters Params) BuildJoinQuery(EntitySchema primarySchema)
    {
        var sql = new StringBuilder();
        var parameters = new DynamicParameters();
        var paramIndex = 0;

        // Track table aliases for disambiguation
        var tableAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [_primaryEntity!] = "t0"
        };

        // Build SELECT clause with aliased columns
        sql.Append("SELECT ");
        var selectColumns = new List<string>();

        // Primary entity columns with alias prefix
        var primaryAlias = tableAliases[_primaryEntity!];
        foreach (var field in primarySchema.Fields.Keys)
        {
            selectColumns.Add($"{primaryAlias}.{field} AS {_primaryEntity}_{field}");
        }

        // Related entity columns
        var aliasIndex = 1;
        var orderedEntities = OrderEntitiesByDependency();
        foreach (var config in orderedEntities)
        {
            var relatedSchema = GetEntitySchema(config.EntityName);
            if (relatedSchema == null) continue;

            var alias = $"t{aliasIndex++}";
            tableAliases[config.EntityName] = alias;

            foreach (var field in relatedSchema.Fields.Keys)
            {
                selectColumns.Add($"{alias}.{field} AS {config.EntityName}_{field}");
            }
        }

        sql.Append(string.Join(", ", selectColumns));

        // FROM clause
        sql.Append($" FROM {primarySchema.TableName} {primaryAlias}");

        // Build JOINs - process in dependency order
        foreach (var config in orderedEntities)
        {
            var relatedSchema = GetEntitySchema(config.EntityName);
            if (relatedSchema == null) continue;

            var relatedAlias = tableAliases[config.EntityName];
            var parentEntityName = config.ParentEntity ?? _primaryEntity!;
            var parentAlias = tableAliases.GetValueOrDefault(parentEntityName, primaryAlias);
            var parentSchema = GetEntitySchema(parentEntityName);

            // Build JOIN clause
            var joinClause = BuildJoinClause(
                relatedSchema,
                config.EntityName,
                relatedAlias,
                parentEntityName,
                parentAlias,
                parentSchema,
                config.Filter,
                parameters,
                ref paramIndex);

            sql.Append(joinClause);
        }

        // WHERE clause for primary entity filter
        var primaryConditions = _primaryFilter!.ToConditions("", primarySchema.AllowedFilterFields).ToList();
        if (primaryConditions.Count > 0)
        {
            var whereClauses = new List<string>();
            foreach (var condition in primaryConditions)
            {
                var (conditionSql, newIndex) = BuildCondition(condition, parameters, paramIndex, primarySchema, primaryAlias);
                whereClauses.Add(conditionSql);
                paramIndex = newIndex;
            }
            sql.Append($" WHERE {string.Join(" AND ", whereClauses)}");
        }

        // ORDER BY primary entity, then related entities by their identifiers for consistent dedup
        var orderClauses = new List<string>();
        orderClauses.Add($"{primaryAlias}.{primarySchema.IdentifierField}");
        
        foreach (var config in orderedEntities)
        {
            var relatedSchema = GetEntitySchema(config.EntityName);
            if (relatedSchema != null)
            {
                var alias = tableAliases[config.EntityName];
                orderClauses.Add($"{alias}.{relatedSchema.IdentifierField}");
            }
        }
        sql.Append($" ORDER BY {string.Join(", ", orderClauses)}");

        // Apply overall LIMIT (database-specific syntax)
        var effectiveLimit = Math.Min(
            _overallLimit > 0 ? _overallLimit.Value : _schemaConfig.DefaultLimit,
            _schemaConfig.MaxLimit);
        
        // SQL Server: OFFSET/FETCH, SQLite: LIMIT
        if (_dbProvider.ProviderName == "SqlServer")
        {
            sql.Append($" OFFSET 0 ROWS FETCH NEXT {effectiveLimit} ROWS ONLY");
        }
        else
        {
            sql.Append($" LIMIT {effectiveLimit}");
        }

        return (sql.ToString(), parameters);
    }

    /// <summary>
    /// Builds a LEFT JOIN clause for a related entity, with optional subquery for entity-level limits.
    /// </summary>
    private string BuildJoinClause(
        EntitySchema relatedSchema,
        string entityName,
        string relatedAlias,
        string parentEntityName,
        string parentAlias,
        EntitySchema? parentSchema,
        EntityFilter? filter,
        DynamicParameters parameters,
        ref int paramIndex)
    {
        // Determine join condition
        var (parentJoinField, childJoinField) = DetermineJoinFields(relatedSchema, entityName, parentEntityName, parentSchema);

        var joinCondition = $"{relatedAlias}.{childJoinField} = {parentAlias}.{parentJoinField}";

        // Check if we need a subquery (entity has $limit)
        var entityLimit = filter?.Limit;

        if (entityLimit > 0)
        {
            // Use subquery with ROW_NUMBER() for entity-level limit
            var subquery = BuildLimitedSubquery(relatedSchema, entityName, childJoinField, filter, parameters, ref paramIndex);
            return $" LEFT JOIN ({subquery}) {relatedAlias} ON {joinCondition}";
        }
        else if (filter?.Conditions.Count > 0)
        {
            // Simple filter - add conditions to JOIN ON clause
            var filterConditions = BuildFilterConditions(relatedSchema, relatedAlias, filter!, parameters, ref paramIndex);
            return $" LEFT JOIN {relatedSchema.TableName} {relatedAlias} ON {joinCondition}{filterConditions}";
        }
        else
        {
            // Simple JOIN without filter
            return $" LEFT JOIN {relatedSchema.TableName} {relatedAlias} ON {joinCondition}";
        }
    }

    /// <summary>
    /// Builds a subquery with row limiting using SQLite window function.
    /// </summary>
    private string BuildLimitedSubquery(
        EntitySchema schema,
        string entityName,
        string partitionField,
        EntityFilter? filter,
        DynamicParameters parameters,
        ref int paramIndex)
    {
        var limit = filter?.Limit ?? _schemaConfig.DefaultLimit;
        var effectiveLimit = Math.Min(limit, _schemaConfig.MaxLimit);

        var sb = new StringBuilder();
        sb.Append($"SELECT *, ROW_NUMBER() OVER (PARTITION BY {partitionField}");

        if (!string.IsNullOrEmpty(schema.DefaultOrderBy))
            sb.Append($" ORDER BY {schema.DefaultOrderBy}");

        sb.Append($") AS _rn FROM {schema.TableName}");

        // Add filter conditions to subquery WHERE
        if (filter?.Conditions.Count > 0)
        {
            var conditions = filter.ToConditions("", schema.AllowedFilterFields);
            var whereClauses = new List<string>();

            foreach (var condition in conditions)
            {
                var (conditionSql, newIndex) = BuildCondition(condition, parameters, paramIndex, schema);
                whereClauses.Add(conditionSql);
                paramIndex = newIndex;
            }

            if (whereClauses.Count > 0)
                sb.Append($" WHERE {string.Join(" AND ", whereClauses)}");
        }

        // Wrap in outer query to apply row limit
        return $"SELECT * FROM ({sb}) WHERE _rn <= {effectiveLimit}";
    }

    /// <summary>
    /// Builds additional filter conditions for JOIN ON clause.
    /// </summary>
    private string BuildFilterConditions(
        EntitySchema schema,
        string tableAlias,
        EntityFilter filter,
        DynamicParameters parameters,
        ref int paramIndex)
    {
        var conditions = filter.ToConditions("", schema.AllowedFilterFields);
        var clauses = new List<string>();

        foreach (var condition in conditions)
        {
            var fieldName = condition.Field.Contains('.') ? condition.Field.Split('.').Last() : condition.Field;
            var field = $"{tableAlias}.{SanitizeFieldName(fieldName)}";
            var fieldType = GetFieldType(schema, fieldName);

            if (condition.IsArray && condition.Value is object[] values)
            {
                var paramNames = new List<string>();
                foreach (var value in values)
                {
                    var paramName = $"p{paramIndex++}";
                    var typedValue = ConvertToTypedValue(value, fieldType, condition.Field);
                    parameters.Add(paramName, typedValue);
                    paramNames.Add($"@{paramName}");
                }
                clauses.Add($"{field} {condition.Operator} ({string.Join(", ", paramNames)})");
            }
            else
            {
                var pName = $"p{paramIndex++}";
                var convertedValue = ConvertToTypedValue(condition.Value, fieldType, condition.Field);
                parameters.Add(pName, convertedValue);
                clauses.Add($"{field} {condition.Operator} @{pName}");
            }
        }

        return clauses.Count > 0 ? " AND " + string.Join(" AND ", clauses) : "";
    }

    /// <summary>
    /// Determines the join fields for parent-child relationship.
    /// Returns (parentField, childField).
    /// </summary>
    private (string ParentField, string ChildField) DetermineJoinFields(
        EntitySchema childSchema,
        string childEntityName,
        string parentEntityName,
        EntitySchema? parentSchema)
    {
        // Check if parent has a relationship to child
        if (parentSchema?.Relationships.TryGetValue(childEntityName, out var parentRel) == true)
        {
            // Parent defines: foreignKey = field in parent, localKey = field in child
            var parentField = parentRel.ForeignKey; // Field in parent table
            var childField = !string.IsNullOrEmpty(parentRel.LocalKey)
                ? parentRel.LocalKey  // Explicit child field
                : parentRel.ForeignKey; // Same field name in child

            return (parentField, childField);
        }

        // Check if child has a relationship back to parent
        if (childSchema.Relationships.TryGetValue(parentEntityName, out var childRel))
        {
            // Child defines: foreignKey = field in child that points to parent
            return (parentSchema?.IdentifierField ?? "id", childRel.ForeignKey);
        }

        // Fallback
        return (parentSchema?.IdentifierField ?? "id", childSchema.IdentifierField);
    }

    /// <summary>
    /// Processes JOIN results into separate entity collections.
    /// Uses column name prefixes to determine which entity each column belongs to.
    /// </summary>
    private void ProcessJoinResults(IEnumerable<dynamic> results, EntitySchema primarySchema, DomainQueryResult result)
    {
        var primaryKey = GetResultKey(_primaryEntity!);
        Dictionary<string, object>? primaryRecord = null;
        var relatedData = new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.OrdinalIgnoreCase);

        // Initialize related entity collections
        foreach (var config in _relatedEntities)
        {
            relatedData[config.EntityName] = new List<Dictionary<string, object>>();
        }

        // Track seen records to avoid duplicates from JOIN
        var seenRelated = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var config in _relatedEntities)
        {
            seenRelated[config.EntityName] = new HashSet<string>();
        }

        foreach (var row in results)
        {
            var rowDict = (IDictionary<string, object>)row;

            // Extract primary entity data (only once)
            if (primaryRecord == null)
            {
                primaryRecord = ExtractEntityData(rowDict, _primaryEntity!, primarySchema);
            }

            // Extract related entity data
            foreach (var config in _relatedEntities)
            {
                var relatedSchema = GetEntitySchema(config.EntityName);
                if (relatedSchema == null) continue;

                var relatedRecord = ExtractEntityData(rowDict, config.EntityName, relatedSchema);

                // Skip if all values are null (no match in LEFT JOIN)
                if (relatedRecord.Values.All(v => v == null || v == DBNull.Value))
                    continue;

                var relatedKeyValue = GetRecordKey(relatedRecord, relatedSchema.IdentifierField);
                if (!string.IsNullOrEmpty(relatedKeyValue) && seenRelated[config.EntityName].Add(relatedKeyValue))
                {
                    // Clean up _rn column if present from subquery
                    relatedRecord.Remove("_rn");
                    relatedData[config.EntityName].Add(relatedRecord);
                }
            }
        }

        // Build result
        result.Data[primaryKey] = primaryRecord;
        result.Counts[primaryKey] = primaryRecord != null ? 1 : 0;

        foreach (var (entityName, records) in relatedData)
        {
            var key = GetResultKey(entityName);
            result.Data[key] = records;
            result.Counts[key] = records.Count;
        }
    }

    /// <summary>
    /// Extracts entity data from a flattened JOIN result row using column prefixes.
    /// </summary>
    private Dictionary<string, object> ExtractEntityData(IDictionary<string, object> row, string entityName, EntitySchema schema)
    {
        var prefix = $"{entityName}_";
        var record = new Dictionary<string, object>();

        foreach (var field in schema.Fields.Keys)
        {
            var columnName = $"{prefix}{field}";
            if (row.TryGetValue(columnName, out var value))
            {
                record[field] = value;
            }
        }

        return record;
    }

    /// <summary>
    /// Gets a unique key for a record to track duplicates.
    /// </summary>
    private static string? GetRecordKey(Dictionary<string, object> record, string identifierField)
    {
        if (record.TryGetValue(identifierField, out var value) && value != null && value != DBNull.Value)
        {
            return value.ToString();
        }
        return null;
    }

    /// <summary>
    /// Orders entities by dependency - parents before children.
    /// </summary>
    private List<RelatedEntityConfig> OrderEntitiesByDependency()
    {
        var ordered = new List<RelatedEntityConfig>();
        var remaining = new List<RelatedEntityConfig>(_relatedEntities);
        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { _primaryEntity! };

        while (remaining.Count > 0)
        {
            var toAdd = remaining.Where(r => resolved.Contains(r.ParentEntity ?? _primaryEntity!)).ToList();

            if (toAdd.Count == 0)
            {
                // Circular dependency or invalid parent - add remaining as-is
                _logger.LogWarning("Unable to resolve dependency order, adding remaining entities");
                ordered.AddRange(remaining);
                break;
            }

            foreach (var entity in toAdd)
            {
                ordered.Add(entity);
                resolved.Add(entity.EntityName);
                remaining.Remove(entity);
            }
        }

        return ordered;
    }

    private (string Sql, int NewParamIndex) BuildCondition(
        FilterCondition condition,
        DynamicParameters parameters,
        int paramIndex,
        EntitySchema schema,
        string? tableAlias = null)
    {
        var fieldName = SanitizeFieldName(condition.Field);
        var field = tableAlias != null ? $"{tableAlias}.{fieldName}" : fieldName;
        var fieldType = GetFieldType(schema, condition.Field);

        // Handle IN/NOT IN with arrays
        if (condition.IsArray && condition.Value is object[] values)
        {
            var paramNames = new List<string>();
            foreach (var value in values)
            {
                var paramName = $"p{paramIndex++}";
                var typedValue = ConvertToTypedValue(value, fieldType, condition.Field);
                parameters.Add(paramName, typedValue);
                paramNames.Add($"@{paramName}");
            }
            return ($"{field} {condition.Operator} ({string.Join(", ", paramNames)})", paramIndex);
        }

        // Handle scalar values
        var pName = $"p{paramIndex++}";
        var convertedValue = ConvertToTypedValue(condition.Value, fieldType, condition.Field);
        parameters.Add(pName, convertedValue);
        return ($"{field} {condition.Operator} @{pName}", paramIndex);
    }

    /// <summary>
    /// Gets the field type from schema, defaulting to "string" if not found.
    /// </summary>
    private static string GetFieldType(EntitySchema schema, string fieldName)
    {
        // Handle prefixed field names
        var cleanFieldName = fieldName.Contains('.') ? fieldName.Split('.').Last() : fieldName;

        if (schema.Fields.TryGetValue(cleanFieldName, out var fieldDef))
            return fieldDef.Type?.ToLowerInvariant() ?? "string";
        return "string";
    }

    /// <summary>
    /// Converts a value to the proper type based on schema field definition.
    /// This ensures proper parameter binding for different database providers.
    /// </summary>
    private static object? ConvertToTypedValue(object? value, string fieldType, string fieldName)
    {
        if (value == null)
            return null;

        try
        {
            return fieldType switch
            {
                "datetime" or "date" => ConvertToDateTime(value),
                "integer" or "int" => ConvertToInteger(value),
                "decimal" or "money" or "numeric" => ConvertToDecimal(value),
                "boolean" or "bool" => ConvertToBoolean(value),
                _ => value // string and unknown types pass through
            };
        }
        catch (Exception ex)
        {
            throw new ArgumentException(
                $"Invalid value for field '{fieldName}' (type: {fieldType}): {value}. {ex.Message}");
        }
    }

    private static object ConvertToDateTime(object value)
    {
        if (value is DateTime dt)
            return dt;

        if (value is string dateStr)
        {
            // Support multiple date formats
            if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces, out var parsed))
                return parsed;

            // Try ISO 8601 explicitly
            if (DateTime.TryParseExact(dateStr,
                new[] { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ", "o" },
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
                return parsed;

            throw new FormatException($"Cannot parse '{dateStr}' as datetime. Use ISO format: yyyy-MM-dd or yyyy-MM-ddTHH:mm:ss");
        }

        throw new FormatException($"Expected datetime value, got {value.GetType().Name}");
    }

    private static object ConvertToInteger(object value)
    {
        return value switch
        {
            int i => i,
            long l => l,
            double d => (long)d,
            string s => long.Parse(s, CultureInfo.InvariantCulture),
            System.Text.Json.JsonElement je => je.GetInt64(),
            _ => Convert.ToInt64(value, CultureInfo.InvariantCulture)
        };
    }

    private static object ConvertToDecimal(object value)
    {
        return value switch
        {
            decimal dec => dec,
            double d => (decimal)d,
            float f => (decimal)f,
            int i => (decimal)i,
            long l => (decimal)l,
            string s => decimal.Parse(s, CultureInfo.InvariantCulture),
            System.Text.Json.JsonElement je => je.GetDecimal(),
            _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture)
        };
    }

    private static object ConvertToBoolean(object value)
    {
        return value switch
        {
            bool b => b,
            string s => bool.Parse(s),
            int i => i != 0,
            System.Text.Json.JsonElement je => je.GetBoolean(),
            _ => Convert.ToBoolean(value, CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Applies Select() filter to remove unwanted entities from result.
    /// Also applies SelectFields() to filter columns within each entity.
    /// </summary>
    private void ApplySelect(DomainQueryResult result)
    {
        // First, remove unwanted entities
        if (_selectedEntities.Count > 0)
        {
            var keysToRemove = result.Data.Keys
                .Where(k => !IsEntitySelected(k))
                .ToList();

            foreach (var key in keysToRemove)
            {
                result.Data.Remove(key);
                result.Counts.Remove(key);
            }
        }

        // Then, filter fields within remaining entities
        if (_selectedFields.Count > 0)
        {
            foreach (var resultKey in result.Data.Keys.ToList())
            {
                var entityName = GetEntityNameFromResultKey(resultKey);
                if (_selectedFields.TryGetValue(entityName, out var allowedFields))
                {
                    var data = result.Data[resultKey];
                    if (data is List<Dictionary<string, object>> records)
                    {
                        // Filter each record to only include selected fields
                        for (int i = 0; i < records.Count; i++)
                        {
                            records[i] = records[i]
                                .Where(kvp => allowedFields.Contains(kvp.Key))
                                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                        }
                    }
                    else if (data is Dictionary<string, object> singleRecord)
                    {
                        // Single record (primary entity)
                        result.Data[resultKey] = singleRecord
                            .Where(kvp => allowedFields.Contains(kvp.Key))
                            .ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets the entity name from a result key (handles pluralization).
    /// </summary>
    private string GetEntityNameFromResultKey(string resultKey)
    {
        // Check all entities to find which one maps to this result key
        foreach (var entityName in _schemaConfig.Entities.Keys)
        {
            if (GetResultKey(entityName).Equals(resultKey, StringComparison.OrdinalIgnoreCase))
                return entityName;
        }
        return resultKey; // Fallback
    }

    /// <summary>
    /// Checks if an entity (by result key) is in the Select() list.
    /// </summary>
    private bool IsEntitySelected(string resultKey)
    {
        // Check against entity names and their result keys
        foreach (var selectedEntity in _selectedEntities)
        {
            if (GetResultKey(selectedEntity).Equals(resultKey, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private EntitySchema? GetEntitySchema(string entityName)
    {
        // Try exact match first, then case-insensitive
        if (_schemaConfig.Entities.TryGetValue(entityName, out var schema))
            return schema;

        var key = _schemaConfig.Entities.Keys
            .FirstOrDefault(k => k.Equals(entityName, StringComparison.OrdinalIgnoreCase));

        return key != null ? _schemaConfig.Entities[key] : null;
    }

    private static string GetResultKey(string entityName)
    {
        // Convert entity name to lowercase for JSON result key
        return entityName.ToLowerInvariant();
    }

    private IDbConnection CreateConnection()
    {
        var connection = _dbProvider.CreateConnection();
        connection.Open();
        return connection;
    }

    private static string SanitizeFieldName(string field)
    {
        // Only allow alphanumeric, underscore, and dot (for table.field)
        return new string(field.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '.').ToArray());
    }
}
