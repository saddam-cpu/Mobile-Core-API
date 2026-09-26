namespace ScreenSharing.Api.Models;

public static class UserRoles
{
    public const string User = "USER";
    public const string Admin = "ADMIN";
}

public static class DeviceStatuses
{
    public const string Online = "ONLINE";
    public const string Offline = "OFFLINE";
    public const string Sharing = "SHARING";
}

public static class SessionStatuses
{
    public const string Active = "ACTIVE";
    public const string Stopped = "STOPPED";
    public const string TerminatedByAdmin = "TERMINATED_BY_ADMIN";
    public const string Failed = "FAILED";
}

public static class PeerConnectionStates
{
    public const string New = "NEW";
    public const string Connecting = "CONNECTING";
    public const string Connected = "CONNECTED";
    public const string Reconnecting = "RECONNECTING";
    public const string Disconnected = "DISCONNECTED";
    public const string Failed = "FAILED";
    public const string Closed = "CLOSED";
}
