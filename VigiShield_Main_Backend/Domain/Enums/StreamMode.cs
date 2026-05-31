namespace VigiShield.Domain.Enums;

public enum StreamMode
{
    /// <summary>Camera is directly accessible via RTSP (fixed IP or local network).</summary>
    DirectRtsp,

    /// <summary>Local PC relays stream via RTMP to the server's MediaMTX (CGNAT / dynamic IP).</summary>
    RtmpRelay
}
