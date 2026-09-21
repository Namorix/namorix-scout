using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Namorix.Core.AddonSession;
using Namorix.Core.Extensions;
using Namorix.Core.Grpc;
using Namorix.Core.OAuth;
using Namorix.Scout.Constants;
using Namorix.Scout.Hubs;
using Namorix.Scout.Persistence;
using Namorix.Scout.Services;
using Namorix.Scout.Streaming;
using static Namorix.Core.Constants.OAuth;

var builder = WebApplication.CreateBuilder(args);

// The default data directory is relative ("./data"), so it is pinned absolute here while the
// original working directory still applies. NmxOAuth2Client reads "{DataDir}/oauth.json" lazily -
// by that point the process has moved into the scratch directory below, and a relative DataDir
// would resolve there instead: credentials lost, and a fresh registration on every boot.
var dataDir = Path.GetFullPath(
    Environment.GetEnvironmentVariable(NmxOAuth2Env.DataDir) ?? NmxOAuth2Defaults.DataDir);
Environment.SetEnvironmentVariable(NmxOAuth2Env.DataDir, dataDir);

var addon = NmxAddonConfig.FromEnvironment();

// Scratch files land in the working directory under names we never choose, so they get a
// directory of their own - one we can delete wholesale without touching the database or keys.
var scratchDir = Path.Combine(dataDir, ScoutDataPaths.HlsScratch);

// Core DI: controllers + JSON (WhenWritingNull) + flat-file logging + rate limiter + notifiers → ScoutHub
builder.Services.AddNamorixCore<ScoutHub>(builder.Environment.IsDevelopment(), o =>
{
    o.DataBasePath = dataDir;
    o.HubPath = SignalRPath.HubScout;
});

builder.Services.AddNmxOAuth2Client();
builder.Services.AddAddonChannelClient();
builder.Services.AddHostedService<ScoutService>();
builder.Services.AddSingleton<CameraChangeSignal>();
builder.Services.AddSingleton<RtspIngestService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RtspIngestService>());
builder.Services.AddSingleton<HlsPackagerRegistry>();
builder.Services.AddHostedService<HlsScratchCleanupService>();

builder.Services.AddDevViteReverseProxy(builder.Environment, builder.Configuration);

builder.Services.AddAddonSessionAuth<ScoutDbContext>(o =>
    builder.Configuration.GetSection(AddonSessionAuthOptions.SectionName).Bind(o));

var dbPath = Path.Combine(dataDir, "scout.db");

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));
builder.Services.AddSingleton<ScoutSecretProtector>();
builder.Services.AddSingleton<CameraService>();

builder.Services.AddDbContextFactory<ScoutDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

var app = builder.Build();

// SharpMP4's muxer writes a scratch file into the current directory for every fragment it emits,
// and there is no public API to point it elsewhere - moving the process here is the only lever.
// ContentRootPath was captured (absolute) when the builder was created, so the static-file root
// below still resolves; every path decided above is already absolute for the same reason.
Directory.CreateDirectory(scratchDir);
Directory.SetCurrentDirectory(scratchDir);

var publicRoot = Path.Combine(app.Environment.ContentRootPath, "public");

app.UseNamorixCore<ScoutHub>(
    configurePipeline: a =>
    {
        a.UseChromeDevToolsProbe404();
        if (!builder.Environment.IsDevelopment() && Directory.Exists(publicRoot))
        {
            a.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = new PhysicalFileProvider(publicRoot)
            });
            a.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(publicRoot)
            });
        }
        a.UseAddonSessionAuth();
    },
    configureEndpoints: e =>
    {
        e.MapDevViteReverseProxy(builder.Environment);
    });

using (var scope = app.Services.CreateScope())
{
    await using var db = await scope.ServiceProvider
        .GetRequiredService<IDbContextFactory<ScoutDbContext>>()
        .CreateDbContextAsync();
    db.Database.Migrate();
}

app.Run();
