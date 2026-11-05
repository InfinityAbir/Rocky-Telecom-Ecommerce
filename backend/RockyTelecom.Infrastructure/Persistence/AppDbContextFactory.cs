using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RockyTelecom.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can build migrations without booting the API host.
/// Uses the LocalDB default unless <c>ROCKY_CONNECTION</c> overrides it (D-20).
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ROCKY_CONNECTION")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=RockyTelecom;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
