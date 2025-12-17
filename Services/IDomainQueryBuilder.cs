using CustomerQueryMcp.Models.Dtos;

namespace CustomerQueryMcp.Services;

/// <summary>
/// Fluent interface for building schema-driven domain queries.
/// Supports MongoDB-style filters for any entity defined in entities.json.
/// Uses JOINs for efficient single-query execution.
/// </summary>
public interface IDomainQueryBuilder
{
    /// <summary>
    /// Creates a fresh builder instance for a new query.
    /// </summary>
    IDomainQueryBuilder Create();

    /// <summary>
    /// Specifies the primary entity to query.
    /// </summary>
    /// <param name="entityName">Entity name as defined in entities.json</param>
    IDomainQueryBuilder From(string entityName);

    /// <summary>
    /// Sets the filter for the primary entity using MongoDB-style syntax.
    /// </summary>
    /// <param name="filter">MongoDB-style filter: { "field": "value", "field2": { "$gte": 100 } }</param>
    IDomainQueryBuilder Where(EntityFilter filter);

    /// <summary>
    /// Adds a related entity with optional MongoDB-style filter.
    /// Use $limit within the filter for entity-level record limits.
    /// </summary>
    /// <param name="entityName">Related entity name as defined in entities.json</param>
    /// <param name="filter">MongoDB-style filter: { "field": "value", "$limit": 5 }</param>
    /// <param name="parent">Parent entity name. If null, defaults to primary entity.</param>
    IDomainQueryBuilder WithRelated(string entityName, EntityFilter? filter = null, string? parent = null);

    /// <summary>
    /// Specifies which entities to include in the response.
    /// If not called, all queried entities are included.
    /// </summary>
    /// <param name="entityNames">Entity names to include in response</param>
    IDomainQueryBuilder Select(params string[] entityNames);

    /// <summary>
    /// Specifies which fields/columns to include for a specific entity.
    /// If not called for an entity, all fields are included.
    /// </summary>
    /// <param name="entityName">Entity name to apply field selection to</param>
    /// <param name="fields">Field names to include</param>
    IDomainQueryBuilder SelectFields(string entityName, params string[] fields);

    /// <summary>
    /// Sets the overall maximum number of result rows from the JOIN query.
    /// This limits the total records before de-duplication.
    /// For entity-specific limits, use $limit in the WithRelated filter.
    /// </summary>
    /// <param name="limit">Maximum rows to return from the JOIN query</param>
    IDomainQueryBuilder Limit(int? limit);

    /// <summary>
    /// Executes the query and returns results.
    /// </summary>
    Task<DomainQueryResult> ExecuteAsync(CancellationToken ct = default);
}
