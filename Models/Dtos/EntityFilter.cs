using System.Text.Json;
using System.Text.Json.Serialization;

namespace CustomerQueryMcp.Models.Dtos;

/// <summary>
/// Generic MongoDB-style filter that works with ANY entity.
/// LLMs already know this syntax - no learning curve!
/// 
/// SYNTAX (MongoDB-style):
/// - Equality: { "field": "value" }
/// - Operators: { "field": { "$gt": value } }
/// 
/// SUPPORTED OPERATORS:
/// - $eq: equals (default if no operator)
/// - $ne: not equals
/// - $gt: greater than
/// - $gte: greater than or equal
/// - $lt: less than
/// - $lte: less than or equal
/// - $in: matches any value in array
/// - $nin: not in array
/// - $like: SQL LIKE pattern (use % wildcard)
/// - $regex: pattern match
/// 
/// EXAMPLES:
/// - Simple: { "status": "active" }
/// - Comparison: { "amount": { "$gte": 100 } }
/// - Range: { "date": { "$gte": "2025-01-01", "$lte": "2025-12-31" } }
/// - Multiple values: { "status": { "$in": ["pending", "shipped"] } }
/// - Combined: { "status": "delivered", "amount": { "$gte": 100 } }
/// - With limit: { "status": "active", "$limit": 5 }
/// </summary>
[JsonConverter(typeof(EntityFilterConverter))]
public class EntityFilter
{
    /// <summary>
    /// Filter conditions as field:value or field:operator pairs.
    /// </summary>
    public Dictionary<string, JsonElement> Conditions { get; set; } = new();

    /// <summary>
    /// Optional limit for this specific entity (overrides global limit).
    /// Can be set via "$limit" in the filter JSON.
    /// </summary>
    public int? Limit { get; set; }

    /// <summary>
    /// Parse filter conditions into SQL-ready field conditions.
    /// </summary>
    public IEnumerable<FilterCondition> ToConditions(string tableName, IEnumerable<string> allowedFields)
    {
        var allowedSet = new HashSet<string>(allowedFields, StringComparer.OrdinalIgnoreCase);

        foreach (var (field, value) in Conditions)
        {
            // Validate field is allowed
            if (!allowedSet.Contains(field))
                continue; // Skip invalid fields silently (or throw if you prefer)

            var fullField = string.IsNullOrEmpty(tableName) ? field : $"{tableName}.{field}";

            if (value.ValueKind == JsonValueKind.Object)
            {
                // Operator object: { "$gte": 100, "$lte": 500 }
                foreach (var prop in value.EnumerateObject())
                {
                    var (op, sqlOp) = ParseOperator(prop.Name);
                    if (op != null)
                    {
                        yield return new FilterCondition
                        {
                            Field = fullField,
                            Operator = sqlOp!,
                            Value = ExtractValue(prop.Value),
                            IsArray = op == "$in" || op == "$nin"
                        };
                    }
                }
            }
            else
            {
                // Simple value: equality
                yield return new FilterCondition
                {
                    Field = fullField,
                    Operator = "=",
                    Value = ExtractValue(value),
                    IsArray = false
                };
            }
        }
    }

    private static (string? op, string? sqlOp) ParseOperator(string mongoOp)
    {
        return mongoOp.ToLowerInvariant() switch
        {
            "$eq" => ("$eq", "="),
            "$ne" => ("$ne", "!="),
            "$gt" => ("$gt", ">"),
            "$gte" => ("$gte", ">="),
            "$lt" => ("$lt", "<"),
            "$lte" => ("$lte", "<="),
            "$in" => ("$in", "IN"),
            "$nin" => ("$nin", "NOT IN"),
            "$like" => ("$like", "LIKE"),
            "$regex" => ("$regex", "LIKE"), // Map regex to LIKE for SQLite
            _ => (null, null)
        };
    }

    private static object? ExtractValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(ExtractValue).ToArray(),
            _ => element.GetRawText()
        };
    }
}

/// <summary>
/// Represents a parsed filter condition ready for SQL generation.
/// </summary>
public class FilterCondition
{
    public string Field { get; set; } = string.Empty;
    public string Operator { get; set; } = "=";
    public object? Value { get; set; }
    public bool IsArray { get; set; }
}

/// <summary>
/// Custom JSON converter to handle EntityFilter deserialization.
/// </summary>
public class EntityFilterConverter : JsonConverter<EntityFilter>
{
    public override EntityFilter? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        var filter = new EntityFilter();

        using var doc = JsonDocument.ParseValue(ref reader);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            // Handle special $limit property
            if (prop.Name.Equals("$limit", StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Value.TryGetInt32(out var limit))
                    filter.Limit = limit;
                continue;
            }
            
            filter.Conditions[prop.Name] = prop.Value.Clone();
        }

        return filter;
    }

    public override void Write(Utf8JsonWriter writer, EntityFilter value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        
        // Write limit if set
        if (value.Limit.HasValue)
            writer.WriteNumber("$limit", value.Limit.Value);
            
        foreach (var (key, val) in value.Conditions)
        {
            writer.WritePropertyName(key);
            val.WriteTo(writer);
        }
        writer.WriteEndObject();
    }
}
