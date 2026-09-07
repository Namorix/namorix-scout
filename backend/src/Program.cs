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

var builder = WebApplication.CreateBuilder(args);

var addon = NmxAddonConfig.FromEnvironment();

// Core DI: controllers + JSON (WhenWritingNull) + flat-file logging + rate limiter + notifiers → ScoutHub
builder.Services.AddNamorixCore<ScoutHub>(builder.Environment.IsDevelopment(), o =>
{
    o.DataBasePath = addon.DataDir;
    o.HubPath = SignalRPath.HubScout;
});

builder.Services.AddNmxOAuth2Client();
builder.Services.AddAddonChannelClient();
builder.Services.AddHostedService<ScoutService>();
builder.Services.AddSingleton<RtspIngestService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RtspIngestService>());
builder.Services.AddSingleton<WebRtcRelayService>();

builder.Services.AddDevViteReverseProxy(builder.Environment, builder.Configuration);

builder.Services.AddAddonSessionAuth<ScoutDbContext>(o =>
    builder.Configuration.GetSection(AddonSessionAuthOptions.SectionName).Bind(o));

var dbPath = Path.Combine(addon.DataDir, "scout.db");

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(addon.DataDir, "keys")));
builder.Services.AddSingleton<ScoutSecretProtector>();
builder.Services.AddSingleton<CameraService>();

builder.Services.AddDbContextFactory<ScoutDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

var app = builder.Build();

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
