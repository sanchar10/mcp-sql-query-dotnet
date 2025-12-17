using CustomerQueryMcp.Models.Dtos;
using CustomerQueryMcp.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace CustomerQueryMcp.Tools;

/// <summary>
/// Domain-focused MCP tools using MongoDB-style filters.
/// LLMs already know this syntax - minimal learning curve!
/// </summary>
[McpServerToolType]
public class DomainQueryTools
{
    private readonly IDomainQueryBuilder _queryBuilder;

    public DomainQueryTools(IDomainQueryBuilder queryBuilder)
    {
        _queryBuilder = queryBuilder;
    }

    /// <summary>
    /// Get complete Customer 360 view.
    /// </summary>
    [McpServerTool(Name = "get_customer_360")]
    [Description("Get complete customer view including profile, subscriptions, products, and interaction history.")]
    public async Task<DomainQueryResult> GetCustomer360(
        [Description("MongoDB-style filter for CustomerProfile. Query by customer_id, email, phone, or name. Example: { \"email\": \"user@example.com\" }")]
        EntityFilter profile,

        [Description("MongoDB-style filter for Subscription. Fields: plan_name, status, start_date, end_date. Use $limit for max results.")]
        EntityFilter? subscription = null,

        [Description("MongoDB-style filter for Product. Fields: product_name, sku, quantity, price. Use $limit for max results.")]
        EntityFilter? product = null,

        [Description("MongoDB-style filter for Interaction. Fields: summary, channel, timestamp. Use $limit for max results.")]
        EntityFilter? interaction = null,

        CancellationToken ct = default)
    {
        return await _queryBuilder.Create()
            .From("CustomerProfile")
            .Where(profile)
            .WithRelated("Subscription", subscription)
            .WithRelated("Product", product, parent: "Subscription")
            .WithRelated("Interaction", interaction)
            .ExecuteAsync(ct);
    }

    /// <summary>
    /// Get customer subscriptions with products.
    /// </summary>
    [McpServerTool(Name = "get_customer_subscriptions")]
    [Description("Get customer subscription details with products.")]
    public async Task<DomainQueryResult> GetCustomerSubscriptions(
        [Description("MongoDB-style filter for CustomerProfile. Query by customer_id, email, phone, or name. Example: { \"customer_id\": \"CUST-0001\" }")]
        EntityFilter profile,

        [Description("MongoDB-style filter for Subscription. Fields: plan_name, status, start_date, end_date. Use $limit for max results.")]
        EntityFilter? subscription = null,

        [Description("MongoDB-style filter for Product. Fields: product_name, sku, quantity, price. Use $limit for max results.")]
        EntityFilter? product = null,

        CancellationToken ct = default)
    {
        return await _queryBuilder.Create()
            .From("CustomerProfile")
            .Where(profile)
            .WithRelated("Subscription", subscription)
            .WithRelated("Product", product, parent: "Subscription")
            .ExecuteAsync(ct);
    }

    /// <summary>
    /// Get subscriptions filtered by product criteria.
    /// </summary>
    [McpServerTool(Name = "get_customer_subscriptions_by_product")]
    [Description("Get customer subscriptions that contain specific products. Filter by product to find matching subscriptions.")]
    public async Task<DomainQueryResult> GetCustomerSubscriptionsByProduct(
        [Description("MongoDB-style filter for CustomerProfile. Query by customer_id, email, phone, or name.")]
        EntityFilter profile,

        [Description("MongoDB-style filter for Product (REQUIRED). Fields: product_name, sku, quantity, price. Example: { \"product_name\": { \"$like\": \"%Office%\" } }")]
        EntityFilter product,

        [Description("MongoDB-style filter for Subscription. Fields: plan_name, status, start_date, end_date.")]
        EntityFilter? subscription = null,

        CancellationToken ct = default)
    {
        return await _queryBuilder.Create()
            .From("CustomerProfile")
            .Where(profile)
            .WithRelated("Subscription", subscription)
            .WithRelated("Product", product, parent: "Subscription")
            .ExecuteAsync(ct);
    }

    /// <summary>
    /// Get customer products only.
    /// </summary>
    [McpServerTool(Name = "get_customer_products")]
    [Description("Get customer products across all subscriptions.")]
    public async Task<DomainQueryResult> GetCustomerProducts(
        [Description("MongoDB-style filter for CustomerProfile. Query by customer_id, email, phone, or name.")]
        EntityFilter profile,

        [Description("MongoDB-style filter for Product. Fields: product_name, sku, quantity, price. Use $limit for max results.")]
        EntityFilter? product = null,

        CancellationToken ct = default)
    {
        return await _queryBuilder.Create()
            .From("CustomerProfile")
            .Where(profile)
            .WithRelated("Subscription")
            .WithRelated("Product", product, parent: "Subscription")
            .Select("Product")
            .ExecuteAsync(ct);
    }

    /// <summary>
    /// Get customer interactions.
    /// </summary>
    [McpServerTool(Name = "get_customer_interactions")]
    [Description("Get customer interaction history.")]
    public async Task<DomainQueryResult> GetCustomerInteractions(
        [Description("MongoDB-style filter for CustomerProfile. Query by customer_id, email, phone, or name.")]
        EntityFilter profile,

        [Description("MongoDB-style filter for Interaction. Fields: summary, channel, timestamp. Use $limit for max results.")]
        EntityFilter? interaction = null,

        CancellationToken ct = default)
    {
        return await _queryBuilder.Create()
            .From("CustomerProfile")
            .Where(profile)
            .WithRelated("Interaction", interaction)
            .ExecuteAsync(ct);
    }

    /// <summary>
    /// Get customer profile only.
    /// </summary>
    [McpServerTool(Name = "get_customer_profile")]
    [Description("Get customer profile without related data.")]
    public async Task<DomainQueryResult> GetCustomerProfile(
        [Description("MongoDB-style filter for CustomerProfile. Query by customer_id, email, phone, or name. Example: { \"phone\": \"+1-555-0100\" }")]
        EntityFilter profile,

        CancellationToken ct = default)
    {
        return await _queryBuilder.Create()
            .From("CustomerProfile")
            .Where(profile)
            .Limit(1)
            .ExecuteAsync(ct);
    }
}
