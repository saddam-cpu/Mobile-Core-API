namespace ScreenSharing.Api.DTOs;

public class SdpMessageDto
{
    public string SessionId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "offer" or "answer"
    public string Sdp { get; set; } = string.Empty;
}

public class IceCandidateMessageDto
{
    public string SessionId { get; set; } = string.Empty;
    public string SdpMid { get; set; } = string.Empty;
    public int SdpMLineIndex { get; set; }
    public string Candidate { get; set; } = string.Empty;
}

public class IceServerConfigDto
{
    public List<string> Urls { get; set; } = new();
    public string? Username { get; set; }
    public string? Credential { get; set; }
}

public class WebRtcConfigResponse
{
    public List<IceServerConfigDto> IceServers { get; set; } = new();
    public string? HostIp { get; set; }
}
