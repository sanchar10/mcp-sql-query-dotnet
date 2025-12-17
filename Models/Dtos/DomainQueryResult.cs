using System.Text.Json.Serialization;

namespace CustomerQueryMcp.Models.Dtos;

/// <summary>
/// Generic result for domain queries - uses Dictionary for extensibility.
/// No per-entity properties needed - just add entities to entities.json!
/// </summary>
public class DomainQueryResult
{
    /// <summary>
    /// Indicates if the query was successful
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    /// <summary>
    /// Error message if the query failed
    /// </summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    /// <summary>
    /// Results keyed by entity name (lowercase).
    /// Primary entity returns single object, related entities return arrays.
    /// Example: { "profile": {...}, "subscription": [...], "order": [...] }
    /// </summary>
    [JsonPropertyName("data")]
    public Dictionary<string, object?> Data { get; set; } = new();

    /// <summary>
    /// Record count per entity
    /// </summary>
    [JsonPropertyName("counts")]
    public Dictionary<string, int> Counts { get; set; } = new();

    /// <summary>
    /// Creates a failed result with an error message.
    /// </summary>
    public static DomainQueryResult Failed(string error) => new()
    {
        Success = false,
        Error = error
    };
}
