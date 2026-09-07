using Microsoft.EntityFrameworkCore;
using Namorix.Core.AddonSession;
using Namorix.Scout.Models;

namespace Namorix.Scout.Persistence;

public sealed class ScoutDbContext(DbContextOptions<ScoutDbContext> options) : AddonSessionDbContext(options)
{
    public DbSet<ScCamera> Cameras => Set<ScCamera>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScCamera>(entity =>
        {
            entity.Property(c => c.StreamType).HasConversion<string>();
        });
    }
}
