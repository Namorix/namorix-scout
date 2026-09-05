using Microsoft.EntityFrameworkCore;
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
builder.Services.AddHostedService<RtspIngestService>();

builder.Services.AddDevViteReverseProxy(builder.Environment, builder.Configuration);

builder.Services.AddAddonSessionAuth<ScoutDbContext>(o =>
    builder.Configuration.GetSection(AddonSessionAuthOptions.SectionName).Bind(o));

var dbPath = Path.Combine(addon.DataDir, "scout.db");

builder.Services.AddDbContextFactory<ScoutDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

var app = builder.Build();

app.UseNamorixCore<ScoutHub>(
    configurePipeline: a =>
    {
        a.UseChromeDevToolsProbe404();
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
