using CustomerQueryMcp.Api;
using CustomerQueryMcp.Data;
using CustomerQueryMcp.Models;
using CustomerQueryMcp.Models.Dtos;
using CustomerQueryMcp.Services;
using CustomerQueryMcp.Tools;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// OpenTelemetry Configuration
// ============================================================
const string serviceName = "CustomerQueryMcp";
const string serviceVersion = "2.0.0";

// Configure OpenTelemetry Resource (identifies this service)
var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(serviceName: serviceName, serviceVersion: serviceVersion);

// Add OpenTelemetry Tracing
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(resourceBuilder)
            .AddSource(serviceName) // Custom activity source for our code
            .AddAspNetCoreInstrumentation(options =>
            {
                // Filter out health checks and swagger from traces
                options.Filter = httpContext =>
                    !httpContext.Request.Path.StartsWithSegments("/health") &&
                    !httpContext.Request.Path.StartsWithSegments("/swagger");
            })
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(options =>
            {
                // Aspire Dashboard OTLP endpoint
                options.Endpoint = new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317");
            });
    })
    .WithMetrics(metrics =>
    {
        metrics
            .SetResourceBuilder(resourceBuilder)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317");
            });
    });

// Add OpenTelemetry Logging
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.SetResourceBuilder(resourceBuilder);
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.AddOtlpExporter(options =>
    {
        options.Endpoint = new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317");
    });
});

// ============================================================
// Database Configuration - Supports SQLite and SQL Server
// ============================================================
var dbProvider = builder.Configuration["Database:Provider"] ?? "SQLite";
var connectionString = dbProvider.ToLowerInvariant() switch
{
    "sqlserver" or "mssql" => builder.Configuration.GetConnectionString("SqlServer") 
        ?? "Server=localhost\\SQLEXPRESS;Database=CustomerMCP;Trusted_Connection=True;TrustServerCertificate=True;",
    _ => builder.Configuration.GetConnectionString("SQLite") 
        ?? "Data Source=Data/customer_data.db"
};

// Create database provider
var databaseProvider = DatabaseProviderFactory.Create(dbProvider, connectionString);

// Log database configuration
Console.WriteLine($"Database Provider: {databaseProvider.ProviderName}");
Console.WriteLine($"Connection: {(databaseProvider.ProviderName == "SqlServer" ? connectionString.Split(';')[0] : connectionString)}");

// Load entity schema configuration
var schemaConfigPath = Path.Combine(AppContext.BaseDirectory, "entities.json");
if (!File.Exists(schemaConfigPath))
{
    schemaConfigPath = "entities.json";
}

EntitySchemaConfig schemaConfig;
try
{
    var schemaJson = await File.ReadAllTextAsync(schemaConfigPath);
    schemaConfig = JsonSerializer.Deserialize<EntitySchemaConfig>(schemaJson, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    }) ?? throw new InvalidOperationException("Failed to parse entities.json");
}
catch (Exception ex)
{
    Console.WriteLine($"Error loading entities.json: {ex.Message}");
    throw;
}

// Register Database Provider as singleton
builder.Services.AddSingleton<IDatabaseProvider>(databaseProvider);

// Register Domain Query Builder (JOIN-based for efficient single-query execution)
builder.Services.AddSingleton<IDomainQueryBuilder>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<DomainQueryBuilder>>();
    return new DomainQueryBuilder(databaseProvider, schemaConfig, logger);
});

// Register tool classes for DI
builder.Services.AddScoped<DomainQueryTools>();

// ============================================================
// Configure MCP Server with domain-focused tools
// ============================================================
builder.Services.AddMcpServer(options =>
{
    options.ServerInfo = new() { Name = "CustomerQueryMCP", Version = "2.0.0" };
})
.WithHttpTransport()
.WithTools<DomainQueryTools>();

// Add Swagger for REST API testing
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Customer Query MCP Server",
        Version = "v1",
        Description = "MCP Server for customer profile and data queries. Supports both MCP protocol and REST API."
    });
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Initialize database with the provider
await DatabaseInitializer.InitializeAsync(databaseProvider, app.Logger);

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Customer Query MCP v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseCors();

// ============================================================
// MCP Endpoint - Single endpoint with all tools
// ============================================================
// NOTE: The MCP C# SDK currently doesn't support multiple named servers
// or tool filtering per endpoint. All agents get all tools.

app.MapMcp("/mcp");

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithTags("Health");

// REST API endpoints (mirrors MCP tools for non-MCP clients)
app.MapCustomerApi();

// Log startup
app.Logger.LogInformation("Customer Query MCP Server starting...");
app.Logger.LogInformation("MCP endpoint: /mcp");
app.Logger.LogInformation("REST API: /api/customer/*");
app.Logger.LogInformation("Swagger UI: /swagger");
app.Logger.LogInformation("Health: /health");

app.Run();
