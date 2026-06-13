using System;

namespace RfidScanner.Models;

public class RfidTag
{
    public string TagId { get; set; } = string.Empty;
    public string TagType { get; set; } = "EPC";
    public int Rssi { get; set; }
    public int ReadCount { get; set; } = 1;
    public DateTime ScannedAt { get; set; } = DateTime.Now;
    public DateTime LastSeen { get; set; } = DateTime.Now;

    public RfidTag Clone() => new()
    {
        TagId = TagId,
        TagType = TagType,
        Rssi = Rssi,
        ReadCount = ReadCount,
        ScannedAt = ScannedAt,
        LastSeen = LastSeen
    };
}
