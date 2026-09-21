namespace Namorix.Scout.Constants;

public static class ScoutDataPaths
{
    // SharpMP4 muxes through scratch files named by the library, dropped in the process working
    // directory - which the app moves into this subdirectory at startup, so they never land
    // beside the database, keys or logs.
    public const string HlsScratch = "hls";
}
