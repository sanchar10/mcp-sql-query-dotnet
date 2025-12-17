using CustomerQueryMcp.Models.Dtos;
using CustomerQueryMcp.Tools;
using Microsoft.AspNetCore.Mvc;

namespace CustomerQueryMcp.Api;

/// <summary>
/// REST API endpoints - mirrors MCP tools for non-MCP clients.
/// </summary>
public static class RestApiEndpoints
{
    /// <summary>
    /// Maps all REST API endpoints for Customer queries.
    /// </summary>
    public static void MapCustomerApi(this WebApplication app)
    {
        var api = app.MapGroup("/api")
            .WithTags("Customer Query REST API");

        api.MapPost("/customer/360", GetCustomer360)
            .WithName("GetCustomer360")
            .WithDescription("Get complete customer view including profile, subscriptions, products, and interaction history.")
            .Produces<DomainQueryResult>(200)
            .Produces<DomainQueryResult>(400);

        api.MapPost("/customer/subscriptions", GetCustomerSubscriptions)
            .WithName("GetCustomerSubscriptions")
            .WithDescription("Get customer subscription details with products.")
            .Produces<DomainQueryResult>(200)
            .Produces<DomainQueryResult>(400);

        api.MapPost("/customer/subscriptions-by-product", GetCustomerSubscriptionsByProduct)
            .WithName("GetCustomerSubscriptionsByProduct")
            .WithDescription("Get customer subscriptions filtered by product criteria.")
            .Produces<DomainQueryResult>(200)
            .Produces<DomainQueryResult>(400);

        api.MapPost("/customer/products", GetCustomerProducts)
            .WithName("GetCustomerProducts")
            .WithDescription("Get customer products across all subscriptions.")
            .Produces<DomainQueryResult>(200)
            .Produces<DomainQueryResult>(400);

        api.MapPost("/customer/interactions", GetCustomerInteractions)
            .WithName("GetCustomerInteractions")
            .WithDescription("Get customer interaction history.")
            .Produces<DomainQueryResult>(200)
            .Produces<DomainQueryResult>(400);

        api.MapPost("/customer/profile", GetCustomerProfile)
            .WithName("GetCustomerProfile")
            .WithDescription("Get customer profile without related data.")
            .Produces<DomainQueryResult>(200)
            .Produces<DomainQueryResult>(400);
    }

    private static async Task<IResult> GetCustomer360(
        DomainQueryTools tools,
        [FromBody] Customer360Request request)
    {
        var result = await tools.GetCustomer360(
            request.Profile,
            request.Subscription,
            request.Product,
            request.Interaction);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    }

    private static async Task<IResult> GetCustomerSubscriptions(
        DomainQueryTools tools,
        [FromBody] CustomerSubscriptionsRequest request)
    {
        var result = await tools.GetCustomerSubscriptions(
            request.Profile,
            request.Subscription,
            request.Product);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    }

    private static async Task<IResult> GetCustomerSubscriptionsByProduct(
        DomainQueryTools tools,
        [FromBody] CustomerSubscriptionsByProductRequest request)
    {
        var result = await tools.GetCustomerSubscriptionsByProduct(
            request.Profile,
            request.Product ?? new EntityFilter(),
            request.Subscription);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    }

    private static async Task<IResult> GetCustomerProducts(
        DomainQueryTools tools,
        [FromBody] CustomerProductsRequest request)
    {
        var result = await tools.GetCustomerProducts(
            request.Profile,
            request.Product);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    }

    private static async Task<IResult> GetCustomerInteractions(
        DomainQueryTools tools,
        [FromBody] CustomerInteractionsRequest request)
    {
        var result = await tools.GetCustomerInteractions(
            request.Profile,
            request.Interaction);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    }

    private static async Task<IResult> GetCustomerProfile(
        DomainQueryTools tools,
        [FromBody] CustomerProfileRequest request)
    {
        var result = await tools.GetCustomerProfile(request.Profile);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    }
}

#region Request Models

/// <summary>
/// Base request with profile filter.
/// </summary>
public class CustomerProfileRequest
{
    /// <summary>
    /// MongoDB-style filter for CustomerProfile. Query by customer_id, email, phone, or name.
    /// Example: { "email": "user@example.com" } or { "customer_id": "CUST-0001" }
    /// </summary>
    public EntityFilter Profile { get; set; } = new();
}

/// <summary>
/// Request model for Customer 360 view.
/// </summary>
public class Customer360Request : CustomerProfileRequest
{
    public EntityFilter? Subscription { get; set; }
    public EntityFilter? Product { get; set; }
    public EntityFilter? Interaction { get; set; }
}

/// <summary>
/// Request model for customer subscriptions with products.
/// </summary>
public class CustomerSubscriptionsRequest : CustomerProfileRequest
{
    public EntityFilter? Subscription { get; set; }
    public EntityFilter? Product { get; set; }
}

/// <summary>
/// Request model for subscriptions filtered by product.
/// </summary>
public class CustomerSubscriptionsByProductRequest : CustomerProfileRequest
{
    public EntityFilter? Product { get; set; }
    public EntityFilter? Subscription { get; set; }
}

/// <summary>
/// Request model for products only.
/// </summary>
public class CustomerProductsRequest : CustomerProfileRequest
{
    public EntityFilter? Product { get; set; }
}

/// <summary>
/// Request model for customer interactions.
/// </summary>
public class CustomerInteractionsRequest : CustomerProfileRequest
{
    public EntityFilter? Interaction { get; set; }
}

#endregion
