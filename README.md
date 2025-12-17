# MCP SQL Query - Schema-Driven Server for AI Agents

A .NET MCP server that provides AI agents with safe, flexible SQL database access using MongoDB-style filters and a schema-driven query builder.

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![MCP](https://img.shields.io/badge/MCP-Compatible-green)](https://modelcontextprotocol.io/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Why This Project?

LLMs can't safely access databases directly, and traditional solutions like GraphQL/OData aren't LLM-friendly. This project combines:

- **Model Context Protocol (MCP)** — Standardized LLM ↔ tool communication
- **Schema-Driven Query Builder** — Generalized, secure SQL generation from JSON config

**Read the full architecture deep-dive:** [Building a Schema-Driven MCP Server](docs/blog-schema-driven-mcp-server.md)

## Features

- 🔍 **MongoDB-style filters** — Syntax LLMs already know
- 🔗 **Nested relationships** — `Customer → Subscription → Product` with automatic JOINs
- 🛡️ **Security by design** — Field-level allowlists prevent unauthorized access
- 🔌 **Dual interface** — MCP for AI agents, REST for traditional apps
- 🗄️ **Multi-database** — SQLite (default) or SQL Server
- 📊 **OpenTelemetry** — Built-in observability

## Quick Start

```bash
git clone https://github.com/sanchar10/mcp-sql-query-dotnet.git
cd mcp-sql-query-dotnet
dotnet run
```

**That's it!** SQLite database is created and seeded automatically.

| Endpoint | URL |
|----------|-----|
| MCP | http://localhost:5000/mcp |
| REST API | http://localhost:5000/api/customer/* |
| Swagger | http://localhost:5000/swagger |

### Test It

```bash
curl -X POST http://localhost:5000/api/customer/360 \
  -H "Content-Type: application/json" \
  -d '{"profile": {"email": "john.doe@example.com"}}'
```

## Available Tools

| Tool | Description |
|------|-------------|
| `get_customer_360` | Complete customer view with subscriptions, products, interactions |
| `get_customer_subscriptions` | Subscriptions with nested products |
| `get_customer_products` | Products across all subscriptions |
| `get_customer_interactions` | Customer interaction history |
| `get_customer_profile` | Profile only |

## Filter Syntax

Uses MongoDB-style operators that LLMs understand:

```json
{
  "profile": { "email": "john@example.com" },
  "subscription": { "status": "active", "$limit": 5 },
  "product": { "price": { "$gte": 100 } }
}
```

| Operator | SQL | Example |
|----------|-----|---------|
| `$eq` | `=` | `{ "status": { "$eq": "active" } }` |
| `$ne` | `!=` | `{ "status": { "$ne": "cancelled" } }` |
| `$gt/$gte` | `>`/`>=` | `{ "price": { "$gte": 100 } }` |
| `$lt/$lte` | `<`/`<=` | `{ "quantity": { "$lt": 5 } }` |
| `$in/$nin` | `IN`/`NOT IN` | `{ "status": { "$in": ["active", "pending"] } }` |
| `$like` | `LIKE` | `{ "name": { "$like": "%John%" } }` |
| `$limit` | `LIMIT` | `{ "$limit": 10 }` |

## Configuration

### Database Provider

SQLite is the default. To use SQL Server, update `appsettings.json`:

```json
{
  "Database": {
    "Provider": "SqlServer"
  },
  "ConnectionStrings": {
    "SqlServer": "Server=localhost\\SQLEXPRESS;Database=CustomerMCP;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

The database is auto-created on first run.

### Adding New Entities

1. Add entity definition to `entities.json`:

```json
{
  "Invoice": {
    "tableName": "Invoice",
    "identifierField": "id",
    "fields": {
      "id": { "type": "integer" },
      "customer_id": { "type": "string" },
      "amount": { "type": "decimal" }
    },
    "allowedFilterFields": ["id", "customer_id", "amount"],
    "relationships": {
      "CustomerProfile": { "foreignKey": "customer_id" }
    }
  }
}
```

2. Add a tool method (~10 lines):

```csharp
[McpServerTool, Description("Get invoices for a customer")]
public async Task<DomainQueryResult> GetCustomerInvoices(
    EntityFilter profile,
    EntityFilter? invoice = null,
    CancellationToken ct = default)
{
    return await _queryBuilder.Create()
        .From("CustomerProfile")
        .Where(profile)
        .WithRelated("Invoice", invoice)
        .ExecuteAsync(ct);
}
```

## Project Structure

```
├── Program.cs                 # Entry point
├── entities.json              # Entity schema definitions
├── appsettings.json           # Configuration
├── Api/                       # REST endpoints
├── Data/                      # Database providers & initialization
├── Models/                    # DTOs and schema models
├── Services/                  # Query builder implementation
└── Tools/                     # MCP tool definitions
```

## Documentation

- **[Architecture Deep-Dive](docs/blog-schema-driven-mcp-server.md)** — Full explanation of the two-pattern approach
- **[MCP Protocol](https://modelcontextprotocol.io/)** — Model Context Protocol specification
- **[C# MCP SDK](https://github.com/modelcontextprotocol/csharp-sdk)** — Official .NET SDK

## Requirements

- .NET 8 SDK
- (Optional) SQL Server Express for production use

## License

MIT

---

**Questions?** Open an issue or check the [detailed documentation](docs/github.md).
