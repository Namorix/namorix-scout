using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Namorix.Scout.Persistence;

public sealed class ScoutDbContextFactory : IDesignTimeDbContextFactory<ScoutDbContext>
{
    public ScoutDbContext CreateDbContext(string[] args)
    {
        var dataDir = Environment.GetEnvironmentVariable("NMX_DATA_DIR") ?? "./data";
        var connection = Environment.GetEnvironmentVariable("SCOUT_DB_CONNECTION")
                         ?? $"Data Source={Path.Combine(dataDir, "scout.db")}";
        var options = new DbContextOptionsBuilder<ScoutDbContext>()
            .UseSqlite(connection)
            .Options;
        return new ScoutDbContext(options);
    }
}
