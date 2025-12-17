using System.Text.Json.Serialization;

namespace CustomerQueryMcp.Models;

/// <summary>
/// Root configuration containing all entity schemas.
/// </summary>
public class EntitySchemaConfig
{
    [JsonPropertyName("entities")]
    public Dictionary<string, EntitySchema> Entities { get; set; } = new();

    [JsonPropertyName("defaultLimit")]
    public int DefaultLimit { get; set; } = 100;

    [JsonPropertyName("maxLimit")]
    public int MaxLimit { get; set; } = 1000;
}

/// <summary>
/// Schema definition for a single entity/table.
/// </summary>
public class EntitySchema
{
    [JsonPropertyName("tableName")]
    public string TableName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("identifierField")]
    public string IdentifierField { get; set; } = string.Empty;

    [JsonPropertyName("fields")]
    public Dictionary<string, FieldDefinition> Fields { get; set; } = new();

    [JsonPropertyName("allowedFilterFields")]
    public List<string> AllowedFilterFields { get; set; } = new();

    [JsonPropertyName("defaultOrderBy")]
    public string? DefaultOrderBy { get; set; }

    [JsonPropertyName("relationships")]
    public Dictionary<string, RelationshipDefinition> Relationships { get; set; } = new();
}

/// <summary>
/// Definition of a single field in an entity.
/// </summary>
public class FieldDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Definition of a relationship between entities.
/// </summary>
public class RelationshipDefinition
{
    [JsonPropertyName("foreignKey")]
    public string ForeignKey { get; set; } = string.Empty;

    [JsonPropertyName("localKey")]
    public string? LocalKey { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "one-to-many"; // one-to-many, many-to-one, one-to-one
}
