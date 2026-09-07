namespace Namorix.Scout.Constants;

public static class SignalRPath
{
    public const string HubPrefix = "/hubs";
    public const string HubScout = $"{HubPrefix}/scout";
}

public static class ScoutSignalRGroups
{
    public const string Scout = "scout";
}

public static class ScoutSignalREvents
{
    public const string CameraChanged = $"{ScoutSignalRGroups.Scout}:camera-changed";
    public const string CameraDeleted = $"{ScoutSignalRGroups.Scout}:camera-deleted";
}
