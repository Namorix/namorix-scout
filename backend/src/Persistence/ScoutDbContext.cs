using Microsoft.EntityFrameworkCore;
using Namorix.Core.AddonSession;
using Namorix.Scout.Models;

namespace Namorix.Scout.Persistence;

public sealed class ScoutDbContext(DbContextOptions<ScoutDbContext> options) : AddonSessionDbContext(options)
{
    // Every camera row carries an owner (ScCamera.UserId) and every query filters on it, so
    // the addon can serve several users at once without one seeing another's cameras.
    public DbSet<ScCamera> Cameras => Set<ScCamera>();

    // Access to a camera is ownership OR a row here, so a share decision is a lookup rather
    // than a copy of the camera.
    public DbSet<CameraShare> CameraShares => Set<CameraShare>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Without this the base's unique index on (ClientId, SessionId) is dropped silently:
        // overriding OnModelCreating replaces the inherited one rather than adding to it.
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ScCamera>(entity =>
        {
            entity.Property(c => c.StreamType).HasConversion<string>();
        });

        modelBuilder.Entity<CameraShare>(entity =>
        {
            // (CameraId, UserId) is the natural key, so it is the primary key rather than a
            // surrogate plus a unique index: "one grant per user per camera" is then a shape
            // the table cannot take, not a rule a caller has to remember.
            entity.HasKey(s => new { s.CameraId, s.UserId });

            // Stored as text so the values survive the enum being reordered, and so the rows
            // stay readable in a sqlite3 session, which is how this addon is debugged.
            entity.Property(s => s.Permission).HasConversion<string>();

            // Deleting a camera must not leave grants pointing at nothing; no query would
            // ever match them, but they would keep the owner's user id around.
            entity.HasOne<ScCamera>()
                .WithMany()
                .HasForeignKey(s => s.CameraId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
