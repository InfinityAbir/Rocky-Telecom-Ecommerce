using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;

namespace RockyTelecom.IntegrationTests;

/// <summary>
/// Hosts the real API against a throw-away SQL Server database (migrations and the demo catalogue are applied
/// on start-up, exactly as in development). CI points it at a SQL Server service through
/// <c>TEST_SQL_CONNECTION</c>; locally it uses LocalDB.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminPhone = "01700000000";
    public const string AdminPassword = "Rocky#2026";

    public static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION")
        ?? "Server=(localdb)\\MSSQLLocalDB;Database=RockyTelecomIntegrationTests;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Seed:AdminPhone", AdminPhone);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("Seed:RunDemoCatalog", "true");
    }

    /// <summary>True when the test database server answers; tests are skipped (not failed) when it does not.</summary>
    public static bool DatabaseAvailable()
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master", ConnectTimeout = 3 };
            using var connection = new SqlConnection(builder.ConnectionString);
            connection.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>[Fact] that reports "skipped" instead of "failed" when no SQL Server is reachable.</summary>
public sealed class SqlFactAttribute : FactAttribute
{
    private static readonly bool Available = ApiFactory.DatabaseAvailable();

    public SqlFactAttribute()
    {
        if (!Available)
        {
            Skip = "No SQL Server reachable (set TEST_SQL_CONNECTION or start LocalDB).";
        }
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
}
