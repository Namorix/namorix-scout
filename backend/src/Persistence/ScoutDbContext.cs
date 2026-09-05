using Microsoft.EntityFrameworkCore;
using Namorix.Core.AddonSession;

namespace Namorix.Scout.Persistence;

public sealed class ScoutDbContext(DbContextOptions<ScoutDbContext> options) : AddonSessionDbContext(options)
{
}
