using Microsoft.EntityFrameworkCore;
using Namorix.Core.AddonSession;
using Namorix.Scout.Models;

namespace Namorix.Scout.Persistence;

public sealed class ScoutDbContext(DbContextOptions<ScoutDbContext> options) : AddonSessionDbContext(options)
{
    // Every camera row carries an owner (ScCamera.UserId) and every query filters on it, so
    // the addon can serve several users at once without one seeing another's cameras.
    public DbSet<ScCamera> Cameras => Set<ScCamera>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Without this the base's unique index on (ClientId, SessionId) is dropped silently:
        // overriding OnModelCreating replaces the inherited one rather than adding to it.
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ScCamera>(entity =>
        {
            entity.Property(c => c.StreamType).HasConversion<string>();
        });
    }
}
