namespace JiYuHelper.Models;

public class TeacherInfo
{
    public string IP { get; set; } = "";
    public string Source { get; set; } = "";
    public string PacketType { get; set; } = "";
    public DateTime DiscoveredAt { get; set; } = DateTime.Now;

    public string Display => $"{IP}  ({Source})";
}
