using Microsoft.EntityFrameworkCore;
using Namorix.Core.AddonSession;
using Namorix.Scout.Models;

namespace Namorix.Scout.Persistence;

public sealed class ScoutDbContext(DbContextOptions<ScoutDbContext> options) : AddonSessionDbContext(options)
{
    // DG9 (temporary): this addon serves ONE user at a time. Camera rows carry no owner, so
    // a second user must be refused rather than served the first user's cameras — the desktop
    // enforces that by rejecting the second grant. Do NOT enable multi-user until ScCamera has
    // a UserId column and every query filters on it.
    public DbSet<ScCamera> Cameras => Set<ScCamera>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Without this the base's unique index on (ClientId, UserId) is dropped silently:
        // overriding OnModelCreating replaces the inherited one rather than adding to it.
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ScCamera>(entity =>
        {
            entity.Property(c => c.StreamType).HasConversion<string>();
        });
    }
}
