using System.Collections.ObjectModel;
using System.Globalization;

namespace SwiftBase.Models;

/// <summary>Demo catalog used by the shell: connections, tables and sample rows.</summary>
public static class SampleData
{
    private static readonly string[] FirstNames =
    {
        "Lina", "Omar", "Sara", "Yousef", "Maya", "Adam", "Nour", "Karim", "Hana", "Zaid",
        "Rami", "Dina", "Tariq", "Aya", "Faris", "Salma", "Bilal", "Rania", "Khalid", "Jana",
    };

    private static readonly string[] LastNames =
    {
        "Haddad", "Nasser", "Khoury", "Mansour", "Saleh", "Fares", "Rahal", "Karam", "Aziz", "Sultan",
    };

    private static readonly string[] Roles = { "admin", "editor", "viewer", "analyst", "support" };
    private static readonly string[] Statuses = { "active", "active", "active", "invited", "suspended" };
    private static readonly string[] OrderStatuses = { "paid", "paid", "pending", "refunded", "cancelled" };
    private static readonly string[] Currencies = { "USD", "EUR", "AED", "SAR" };
    private static readonly string[] Categories = { "Hardware", "Software", "Services", "Accessories", "Cloud" };

    public static ObservableCollection<ConnectionGroup> CreateConnections() => new()
    {
        new ConnectionGroup
        {
            Name = "Local projects",
            Items = new[]
            {
                new ConnectionProfile
                {
                    Id = "demo-local",
                    Name = "Local playground",
                    Engine = DbEngine.Sqlite,
                    Database = "local.db",
                    Environment = "Local",
                    IsDemo = true,
                },
                new ConnectionProfile
                {
                    Id = "demo-analytics",
                    Name = "Analytics scratch",
                    Engine = DbEngine.Postgres,
                    Host = "127.0.0.1:5432",
                    Database = "analytics",
                    Environment = "Local",
                    IsDemo = true,
                },
            },
        },
        new ConnectionGroup
        {
            Name = "Work server",
            Items = new[]
            {
                new ConnectionProfile
                {
                    Id = "demo-staging",
                    Name = "Staging · EU",
                    Engine = DbEngine.Postgres,
                    Host = "staging-eu.internal",
                    Database = "shop",
                    Environment = "Staging",
                    IsDemo = true,
                },
                new ConnectionProfile
                {
                    Id = "demo-production",
                    Name = "Shop production",
                    Engine = DbEngine.MySql,
                    Host = "db.shop.example",
                    Database = "shop_prod",
                    Environment = "Production",
                    IsDemo = true,
                },
            },
        },
    };

    public static ObservableCollection<DbTable> CreateTables()
    {
        var tables = new ObservableCollection<DbTable>
        {
            CreateUsers(),
            CreateOrders(),
            CreateProducts(),
            CreateActiveUsersView(),
        };
        return tables;
    }

    private static DbTable CreateUsers()
    {
        var table = new DbTable
        {
            Name = "users",
            Kind = TableKind.Table,
            Schema = "public",
            Columns = new[]
            {
                new DbColumn { Name = "id", DataType = "uuid", IsPrimaryKey = true, IsNullable = false },
                new DbColumn { Name = "full_name", DataType = "varchar(80)", IsNullable = false, DefaultValue = "" },
                new DbColumn { Name = "email", DataType = "varchar(160)", IsNullable = false },
                new DbColumn { Name = "role", DataType = "varchar(20)", IsNullable = false, DefaultValue = "'viewer'" },
                new DbColumn { Name = "status", DataType = "varchar(12)", IsNullable = false, DefaultValue = "'active'" },
                new DbColumn { Name = "last_login", DataType = "timestamptz", IsNullable = true },
                new DbColumn { Name = "notes", DataType = "jsonb", IsNullable = true },
            },
        };

        var random = new Random(42);
        var start = new DateTime(2026, 11, 18, 9, 30, 0);
        for (var i = 0; i < 2000; i++)
        {
            var first = FirstNames[i % FirstNames.Length];
            var last = LastNames[(i / 3) % LastNames.Length];
            var name = $"{first} {last}";
            var email = $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}{i:0000}@swiftbase.dev";
            var role = Roles[(i * 7) % Roles.Length];
            var status = Statuses[(i * 5) % Statuses.Length];
            var login = start.AddMinutes(-random.Next(5, 60 * 24 * 21));
            var notes = i % 9 == 0
                ? "{\"plan\":\"pro\",\"seats\":12,\"tags\":[\"beta\",\"priority\"],\"billing\":{\"cycle\":\"yearly\",\"renewal\":\"2027-02-01\"}}"
                : i % 13 == 0
                    ? "Long-form note: customer asked for a bulk import walkthrough and a follow-up call next Tuesday."
                    : null;

            var row = new RowViewModel(new Dictionary<string, string?>
            {
                ["id"] = $"usr_{i + 1:00000}",
                ["full_name"] = name,
                ["email"] = email,
                ["role"] = role,
                ["status"] = status,
                ["last_login"] = i % 11 == 0 ? null : login.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ["notes"] = notes,
            });
            table.Rows.Add(row);
        }

        return table;
    }

    private static DbTable CreateOrders()
    {
        var table = new DbTable
        {
            Name = "orders",
            Kind = TableKind.Table,
            Schema = "public",
            Columns = new[]
            {
                new DbColumn { Name = "id", DataType = "uuid", IsPrimaryKey = true, IsNullable = false },
                new DbColumn { Name = "user_id", DataType = "uuid", IsNullable = false },
                new DbColumn { Name = "total", DataType = "numeric(10,2)", IsNullable = false },
                new DbColumn { Name = "currency", DataType = "char(3)", IsNullable = false, DefaultValue = "'USD'" },
                new DbColumn { Name = "status", DataType = "varchar(16)", IsNullable = false },
                new DbColumn { Name = "created_at", DataType = "timestamptz", IsNullable = false },
                new DbColumn { Name = "payload", DataType = "jsonb", IsNullable = true },
            },
        };

        var random = new Random(7);
        var start = new DateTime(2026, 11, 18, 12, 0, 0);
        for (var i = 0; i < 360; i++)
        {
            var created = start.AddHours(-random.Next(1, 24 * 60));
            var total = random.Next(1200, 98000) / 100m;
            var payload = i % 4 == 0
                ? "{\"channel\":\"web\",\"coupon\":\"SWIFT10\",\"items\":[{\"sku\":\"SB-100\",\"qty\":2}]}"
                : "";
            var row = new RowViewModel(new Dictionary<string, string?>
            {
                ["id"] = $"ord_{i + 1:00000}",
                ["user_id"] = $"usr_{random.Next(1, 2000):00000}",
                ["total"] = total.ToString("0.00", CultureInfo.InvariantCulture),
                ["currency"] = Currencies[(i * 3) % Currencies.Length],
                ["status"] = OrderStatuses[(i * 5) % OrderStatuses.Length],
                ["created_at"] = created.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ["payload"] = payload,
            });
            table.Rows.Add(row);
        }

        return table;
    }

    private static DbTable CreateProducts()
    {
        var table = new DbTable
        {
            Name = "products",
            Kind = TableKind.Table,
            Schema = "public",
            Columns = new[]
            {
                new DbColumn { Name = "id", DataType = "uuid", IsPrimaryKey = true, IsNullable = false },
                new DbColumn { Name = "sku", DataType = "varchar(24)", IsNullable = false },
                new DbColumn { Name = "title", DataType = "varchar(140)", IsNullable = false },
                new DbColumn { Name = "price", DataType = "numeric(10,2)", IsNullable = false },
                new DbColumn { Name = "stock", DataType = "int4", IsNullable = false, DefaultValue = "0" },
                new DbColumn { Name = "category", DataType = "varchar(40)", IsNullable = true },
                new DbColumn { Name = "updated_at", DataType = "timestamptz", IsNullable = true },
                new DbColumn { Name = "meta", DataType = "jsonb", IsNullable = true },
            },
        };

        var random = new Random(11);
        for (var i = 0; i < 84; i++)
        {
            var category = Categories[i % Categories.Length];
            var row = new RowViewModel(new Dictionary<string, string?>
            {
                ["id"] = $"prd_{i + 1:00000}",
                ["sku"] = $"SB-{100 + i:000}",
                ["title"] = $"{category} pack #{i + 1}",
                ["price"] = (random.Next(900, 42000) / 100m).ToString("0.00", CultureInfo.InvariantCulture),
                ["stock"] = random.Next(0, 480).ToString(CultureInfo.InvariantCulture),
                ["category"] = category,
                ["updated_at"] = new DateTime(2026, 11, 18).AddDays(-random.Next(0, 90))
                    .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ["meta"] = i % 5 == 0 ? "{\"warehouse\":\"DXB-1\",\"fragile\":false}" : "",
            });
            table.Rows.Add(row);
        }

        return table;
    }

    private static DbTable CreateActiveUsersView()
    {
        var table = new DbTable
        {
            Name = "active_users",
            Kind = TableKind.View,
            Schema = "public",
            Columns = new[]
            {
                new DbColumn { Name = "id", DataType = "uuid", IsPrimaryKey = true, IsNullable = false },
                new DbColumn { Name = "full_name", DataType = "varchar(80)", IsNullable = false },
                new DbColumn { Name = "email", DataType = "varchar(160)", IsNullable = false },
                new DbColumn { Name = "role", DataType = "varchar(20)", IsNullable = false },
                new DbColumn { Name = "last_login", DataType = "timestamptz", IsNullable = true },
            },
        };

        var random = new Random(19);
        for (var i = 0; i < 48; i++)
        {
            var first = FirstNames[(i * 3) % FirstNames.Length];
            var last = LastNames[(i * 7) % LastNames.Length];
            var row = new RowViewModel(new Dictionary<string, string?>
            {
                ["id"] = $"usr_v{i + 1:0000}",
                ["full_name"] = $"{first} {last}",
                ["email"] = $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}.v{i}@swiftbase.dev",
                ["role"] = Roles[(i * 2) % Roles.Length],
                ["last_login"] = new DateTime(2026, 11, 18, 8, 0, 0)
                    .AddMinutes(-random.Next(10, 5000))
                    .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            });
            table.Rows.Add(row);
        }

        return table;
    }
}
