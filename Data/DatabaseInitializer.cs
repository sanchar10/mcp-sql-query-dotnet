using Dapper;
using Microsoft.Data.SqlClient;

namespace CustomerQueryMcp.Data;

/// <summary>
/// Initializes the database schema and seeds sample data.
/// Supports both SQLite and SQL Server.
/// Automatically creates the database if it doesn't exist (SQL Server).
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IDatabaseProvider provider, ILogger logger)
    {
        // For SQL Server, ensure database exists first
        if (provider.ProviderName == "SqlServer")
        {
            await EnsureSqlServerDatabaseExistsAsync(provider.ConnectionString, logger);
        }

        using var connection = provider.CreateConnection();
        connection.Open();

        logger.LogInformation("Initializing {Provider} database...", provider.ProviderName);

        // Create tables based on provider
        if (provider.ProviderName == "SqlServer")
        {
            await CreateSqlServerTablesAsync(connection);
        }
        else
        {
            await CreateSqliteTablesAsync(connection);
        }

        // Check if data exists
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM CustomerProfile");
        if (count == 0)
        {
            logger.LogInformation("Seeding sample data...");
            await SeedDataAsync(connection, provider);
            logger.LogInformation("Sample data seeded successfully");
        }
        else
        {
            logger.LogInformation("Database already contains data, skipping seed");
        }
    }

    /// <summary>
    /// Ensures the SQL Server database exists, creating it if necessary.
    /// This allows zero-configuration startup for developers with SQL Server Express.
    /// </summary>
    private static async Task EnsureSqlServerDatabaseExistsAsync(string connectionString, ILogger logger)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;
        
        if (string.IsNullOrEmpty(databaseName))
        {
            logger.LogWarning("No database name specified in connection string. Skipping database creation.");
            return;
        }

        // Connect to master database to check/create target database
        builder.InitialCatalog = "master";
        var masterConnectionString = builder.ConnectionString;

        try
        {
            using var connection = new SqlConnection(masterConnectionString);
            await connection.OpenAsync();

            // Check if database exists
            var exists = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.databases WHERE name = @dbName",
                new { dbName = databaseName });

            if (exists == 0)
            {
                logger.LogInformation("Creating database '{DatabaseName}'...", databaseName);
                
                // Create database (use dynamic SQL since database names can't be parameterized)
                var safeDatabaseName = databaseName.Replace("'", "''").Replace("[", "").Replace("]", "");
                await connection.ExecuteAsync($"CREATE DATABASE [{safeDatabaseName}]");
                
                logger.LogInformation("Database '{DatabaseName}' created successfully", databaseName);
            }
            else
            {
                logger.LogDebug("Database '{DatabaseName}' already exists", databaseName);
            }
        }
        catch (SqlException ex) when (ex.Number == 1801) // Database already exists (race condition)
        {
            logger.LogDebug("Database '{DatabaseName}' was created by another process", databaseName);
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "Failed to create database '{DatabaseName}'. Ensure SQL Server is running and you have CREATE DATABASE permissions.", databaseName);
            throw;
        }
    }

    private static async Task CreateSqlServerTablesAsync(System.Data.IDbConnection connection)
    {
        // CustomerProfile table
        await connection.ExecuteAsync(@"
            IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='CustomerProfile' AND xtype='U')
            CREATE TABLE CustomerProfile (
                customer_id NVARCHAR(50) PRIMARY KEY,
                name NVARCHAR(200) NOT NULL,
                email NVARCHAR(200) UNIQUE,
                phone NVARCHAR(50) UNIQUE,
                created_at DATETIME2 NOT NULL
            )");

        // Interaction table (customer interaction history)
        await connection.ExecuteAsync(@"
            IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Interaction' AND xtype='U')
            CREATE TABLE Interaction (
                id INT IDENTITY(1,1) PRIMARY KEY,
                customer_id NVARCHAR(50) NOT NULL,
                summary NVARCHAR(MAX) NOT NULL,
                channel NVARCHAR(50) NOT NULL,
                timestamp DATETIME2 NOT NULL,
                FOREIGN KEY (customer_id) REFERENCES CustomerProfile(customer_id)
            )");

        // Subscription table
        await connection.ExecuteAsync(@"
            IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Subscription' AND xtype='U')
            CREATE TABLE Subscription (
                id INT IDENTITY(1,1) PRIMARY KEY,
                customer_id NVARCHAR(50) NOT NULL,
                plan_name NVARCHAR(100) NOT NULL,
                status NVARCHAR(50) NOT NULL,
                start_date DATETIME2 NOT NULL,
                end_date DATETIME2,
                FOREIGN KEY (customer_id) REFERENCES CustomerProfile(customer_id)
            )");

        // Product table (child of Subscription)
        await connection.ExecuteAsync(@"
            IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Product' AND xtype='U')
            CREATE TABLE Product (
                id INT IDENTITY(1,1) PRIMARY KEY,
                subscription_id INT NOT NULL,
                product_name NVARCHAR(200) NOT NULL,
                sku NVARCHAR(50) NOT NULL,
                quantity INT NOT NULL DEFAULT 1,
                price DECIMAL(18,2) NOT NULL,
                FOREIGN KEY (subscription_id) REFERENCES Subscription(id)
            )");

        // Create indexes for better query performance
        await connection.ExecuteAsync(@"
            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='idx_profile_email')
            CREATE INDEX idx_profile_email ON CustomerProfile(email)");
        await connection.ExecuteAsync(@"
            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='idx_profile_phone')
            CREATE INDEX idx_profile_phone ON CustomerProfile(phone)");
        await connection.ExecuteAsync(@"
            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='idx_interaction_customerid')
            CREATE INDEX idx_interaction_customerid ON Interaction(customer_id)");
        await connection.ExecuteAsync(@"
            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='idx_interaction_timestamp')
            CREATE INDEX idx_interaction_timestamp ON Interaction(timestamp)");
        await connection.ExecuteAsync(@"
            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='idx_subscription_customerid')
            CREATE INDEX idx_subscription_customerid ON Subscription(customer_id)");
        await connection.ExecuteAsync(@"
            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='idx_product_subscription')
            CREATE INDEX idx_product_subscription ON Product(subscription_id)");
    }

    private static async Task CreateSqliteTablesAsync(System.Data.IDbConnection connection)
    {
        // CustomerProfile table
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS CustomerProfile (
                customer_id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                email TEXT UNIQUE,
                phone TEXT UNIQUE,
                created_at TEXT NOT NULL
            )");

        // Interaction table (customer interaction history)
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Interaction (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                customer_id TEXT NOT NULL,
                summary TEXT NOT NULL,
                channel TEXT NOT NULL,
                timestamp TEXT NOT NULL,
                FOREIGN KEY (customer_id) REFERENCES CustomerProfile(customer_id)
            )");

        // Subscription table
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Subscription (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                customer_id TEXT NOT NULL,
                plan_name TEXT NOT NULL,
                status TEXT NOT NULL,
                start_date TEXT NOT NULL,
                end_date TEXT,
                FOREIGN KEY (customer_id) REFERENCES CustomerProfile(customer_id)
            )");

        // Product table (child of Subscription)
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Product (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                subscription_id INTEGER NOT NULL,
                product_name TEXT NOT NULL,
                sku TEXT NOT NULL,
                quantity INTEGER NOT NULL DEFAULT 1,
                price REAL NOT NULL,
                FOREIGN KEY (subscription_id) REFERENCES Subscription(id)
            )");

        // Create indexes for better query performance
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_profile_email ON CustomerProfile(email)");
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_profile_phone ON CustomerProfile(phone)");
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_interaction_customerid ON Interaction(customer_id)");
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_interaction_timestamp ON Interaction(timestamp)");
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_subscription_customerid ON Subscription(customer_id)");
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_product_subscription ON Product(subscription_id)");
    }

    private static async Task SeedDataAsync(System.Data.IDbConnection connection, IDatabaseProvider provider)
    {
        var now = DateTime.UtcNow;

        // Sample customers
        var customers = new[]
        {
            new { customer_id = "C001", name = "John Doe", email = "john.doe@example.com", phone = "+1-555-0101", created_at = now.AddDays(-30) },
            new { customer_id = "C002", name = "Jane Smith", email = "jane.smith@example.com", phone = "+1-555-0102", created_at = now.AddDays(-25) },
            new { customer_id = "C003", name = "Bob Wilson", email = "bob.wilson@example.com", phone = "+1-555-0103", created_at = now.AddDays(-20) },
            new { customer_id = "C004", name = "Alice Johnson", email = "alice.johnson@example.com", phone = "+1-555-0104", created_at = now.AddDays(-15) },
            new { customer_id = "C005", name = "Charlie Brown", email = "charlie.brown@example.com", phone = "+1-555-0105", created_at = now.AddDays(-10) }
        };

        foreach (var customer in customers)
        {
            await connection.ExecuteAsync(
                "INSERT INTO CustomerProfile (customer_id, name, email, phone, created_at) VALUES (@customer_id, @name, @email, @phone, @created_at)",
                customer);
        }

        // Sample interactions
        var interactions = new[]
        {
            new { customer_id = "C001", summary = "Customer inquired about product pricing for enterprise plan", channel = "email", timestamp = now.AddDays(-5) },
            new { customer_id = "C001", summary = "Follow-up call scheduled for next week regarding demo", channel = "phone", timestamp = now.AddDays(-3) },
            new { customer_id = "C001", summary = "Customer requested technical documentation", channel = "chat", timestamp = now.AddDays(-1) },
            new { customer_id = "C002", summary = "Discussed integration requirements with cloud services", channel = "email", timestamp = now.AddDays(-4) },
            new { customer_id = "C002", summary = "Sent proposal for custom implementation", channel = "email", timestamp = now.AddDays(-2) },
            new { customer_id = "C003", summary = "Support ticket opened for login issues", channel = "chat", timestamp = now.AddDays(-6) },
            new { customer_id = "C003", summary = "Issue resolved, password reset completed", channel = "chat", timestamp = now.AddDays(-5) },
            new { customer_id = "C004", summary = "Requested information about API rate limits", channel = "email", timestamp = now.AddDays(-3) },
            new { customer_id = "C004", summary = "Upgraded to premium tier for higher limits", channel = "phone", timestamp = now.AddDays(-1) },
            new { customer_id = "C005", summary = "New customer onboarding call completed", channel = "phone", timestamp = now.AddDays(-8) },
            new { customer_id = "C005", summary = "Training session scheduled for team", channel = "email", timestamp = now.AddDays(-6) },
            new { customer_id = "C005", summary = "First project deployment successful", channel = "chat", timestamp = now.AddDays(-2) }
        };

        foreach (var interaction in interactions)
        {
            await connection.ExecuteAsync(
                "INSERT INTO Interaction (customer_id, summary, channel, timestamp) VALUES (@customer_id, @summary, @channel, @timestamp)",
                interaction);
        }

        // Sample subscriptions (1-3 per user to demonstrate multiple subscriptions)
        var subscriptions = new[]
        {
            // C001 - 3 subscriptions (1 active, 1 cancelled, 1 expired)
            new { customer_id = "C001", plan_name = "Starter", status = "expired", start_date = now.AddDays(-365), end_date = (DateTime?)now.AddDays(-30) },
            new { customer_id = "C001", plan_name = "Professional", status = "cancelled", start_date = now.AddDays(-180), end_date = (DateTime?)now.AddDays(-60) },
            new { customer_id = "C001", plan_name = "Enterprise", status = "active", start_date = now.AddDays(-30), end_date = (DateTime?)now.AddDays(335) },
            
            // C002 - 2 subscriptions (1 expired, 1 active)
            new { customer_id = "C002", plan_name = "Starter", status = "expired", start_date = now.AddDays(-400), end_date = (DateTime?)now.AddDays(-35) },
            new { customer_id = "C002", plan_name = "Professional", status = "active", start_date = now.AddDays(-25), end_date = (DateTime?)now.AddDays(340) },
            
            // C003 - 1 subscription (active)
            new { customer_id = "C003", plan_name = "Starter", status = "active", start_date = now.AddDays(-20), end_date = (DateTime?)now.AddDays(345) },
            
            // C004 - 3 subscriptions (2 expired, 1 active showing upgrade path)
            new { customer_id = "C004", plan_name = "Starter", status = "expired", start_date = now.AddDays(-300), end_date = (DateTime?)now.AddDays(-200) },
            new { customer_id = "C004", plan_name = "Professional", status = "expired", start_date = now.AddDays(-200), end_date = (DateTime?)now.AddDays(-5) },
            new { customer_id = "C004", plan_name = "Premium", status = "active", start_date = now.AddDays(-1), end_date = (DateTime?)now.AddDays(364) },
            
            // C005 - 2 subscriptions (1 cancelled, 1 active)
            new { customer_id = "C005", plan_name = "Professional", status = "cancelled", start_date = now.AddDays(-100), end_date = (DateTime?)now.AddDays(-50) },
            new { customer_id = "C005", plan_name = "Enterprise", status = "active", start_date = now.AddDays(-8), end_date = (DateTime?)now.AddDays(357) }
        };

        foreach (var sub in subscriptions)
        {
            await connection.ExecuteAsync(
                "INSERT INTO Subscription (customer_id, plan_name, status, start_date, end_date) VALUES (@customer_id, @plan_name, @status, @start_date, @end_date)",
                sub);
        }

        // Get subscription IDs for product seeding
        var subscriptionIds = (await connection.QueryAsync<int>("SELECT id FROM Subscription ORDER BY id")).ToList();

        // Sample products (2-4 products per subscription to demonstrate nested relationships)
        var products = new List<object>();
        
        // Products for subscription 1 (C001 - Starter expired)
        if (subscriptionIds.Count > 0)
        {
            products.Add(new { subscription_id = subscriptionIds[0], product_name = "CloudSuite Basic", sku = "CS-BAS", quantity = 5, price = 6.99m });
            products.Add(new { subscription_id = subscriptionIds[0], product_name = "Cloud Storage 100GB", sku = "STR-100", quantity = 1, price = 1.99m });
        }

        // Products for subscription 2 (C001 - Professional cancelled)
        if (subscriptionIds.Count > 1)
        {
            products.Add(new { subscription_id = subscriptionIds[1], product_name = "CloudSuite Pro", sku = "CS-PRO", quantity = 10, price = 23.00m });
            products.Add(new { subscription_id = subscriptionIds[1], product_name = "Identity Manager P1", sku = "IDM-P1", quantity = 10, price = 6.00m });
            products.Add(new { subscription_id = subscriptionIds[1], product_name = "Voice Connect", sku = "VC-STD", quantity = 5, price = 8.00m });
        }

        // Products for subscription 3 (C001 - Enterprise active)
        if (subscriptionIds.Count > 2)
        {
            products.Add(new { subscription_id = subscriptionIds[2], product_name = "CloudSuite Enterprise", sku = "CS-ENT", quantity = 25, price = 38.00m });
            products.Add(new { subscription_id = subscriptionIds[2], product_name = "Identity Manager P2", sku = "IDM-P2", quantity = 25, price = 9.00m });
            products.Add(new { subscription_id = subscriptionIds[2], product_name = "Security Shield", sku = "SEC-ENT", quantity = 25, price = 5.20m });
            products.Add(new { subscription_id = subscriptionIds[2], product_name = "Analytics Pro", sku = "ANL-PRO", quantity = 10, price = 10.00m });
        }

        // Products for subscription 4 (C002 - Starter expired)
        if (subscriptionIds.Count > 3)
        {
            products.Add(new { subscription_id = subscriptionIds[3], product_name = "CloudSuite Basic", sku = "CS-BAS", quantity = 3, price = 6.99m });
        }

        // Products for subscription 5 (C002 - Professional active)
        if (subscriptionIds.Count > 4)
        {
            products.Add(new { subscription_id = subscriptionIds[4], product_name = "CloudSuite Pro", sku = "CS-PRO", quantity = 15, price = 23.00m });
            products.Add(new { subscription_id = subscriptionIds[4], product_name = "DevOps Platform", sku = "DEV-BAS", quantity = 10, price = 6.00m });
            products.Add(new { subscription_id = subscriptionIds[4], product_name = "Code Repository Enterprise", sku = "CR-ENT", quantity = 10, price = 21.00m });
        }

        // Products for subscription 6 (C003 - Starter active)
        if (subscriptionIds.Count > 5)
        {
            products.Add(new { subscription_id = subscriptionIds[5], product_name = "CloudSuite Basic", sku = "CS-BAS", quantity = 2, price = 6.99m });
            products.Add(new { subscription_id = subscriptionIds[5], product_name = "Cloud Storage 100GB", sku = "STR-100", quantity = 2, price = 1.99m });
        }

        // Products for subscription 7 (C004 - Starter expired)
        if (subscriptionIds.Count > 6)
        {
            products.Add(new { subscription_id = subscriptionIds[6], product_name = "CloudSuite Basic", sku = "CS-BAS", quantity = 1, price = 6.99m });
        }

        // Products for subscription 8 (C004 - Professional expired)
        if (subscriptionIds.Count > 7)
        {
            products.Add(new { subscription_id = subscriptionIds[7], product_name = "CloudSuite Pro", sku = "CS-PRO", quantity = 5, price = 23.00m });
            products.Add(new { subscription_id = subscriptionIds[7], product_name = "CRM Platform", sku = "CRM-STD", quantity = 3, price = 95.00m });
        }

        // Products for subscription 9 (C004 - Premium active)
        if (subscriptionIds.Count > 8)
        {
            products.Add(new { subscription_id = subscriptionIds[8], product_name = "CloudSuite Enterprise", sku = "CS-ENT", quantity = 10, price = 38.00m });
            products.Add(new { subscription_id = subscriptionIds[8], product_name = "CRM Platform", sku = "CRM-STD", quantity = 5, price = 95.00m });
            products.Add(new { subscription_id = subscriptionIds[8], product_name = "Automation Platform", sku = "AUTO-PRO", quantity = 5, price = 40.00m });
        }

        // Products for subscription 10 (C005 - Professional cancelled)
        if (subscriptionIds.Count > 9)
        {
            products.Add(new { subscription_id = subscriptionIds[9], product_name = "CloudSuite Pro", sku = "CS-PRO", quantity = 8, price = 23.00m });
        }

        // Products for subscription 11 (C005 - Enterprise active)
        if (subscriptionIds.Count > 10)
        {
            products.Add(new { subscription_id = subscriptionIds[10], product_name = "CloudSuite Enterprise", sku = "CS-ENT", quantity = 20, price = 38.00m });
            products.Add(new { subscription_id = subscriptionIds[10], product_name = "Identity Manager P2", sku = "IDM-P2", quantity = 20, price = 9.00m });
            products.Add(new { subscription_id = subscriptionIds[10], product_name = "AI Assistant", sku = "AI-PRO", quantity = 10, price = 30.00m });
        }

        foreach (var product in products)
        {
            await connection.ExecuteAsync(
                "INSERT INTO Product (subscription_id, product_name, sku, quantity, price) VALUES (@subscription_id, @product_name, @sku, @quantity, @price)",
                product);
        }
    }
}
