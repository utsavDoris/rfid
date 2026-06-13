using System;

namespace RfidScanner.Models;

public class RfidTag
{
    public string Epc { get; set; } = string.Empty;
    public string Tid { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string TagType { get; set; } = "EPC";
    public int Rssi { get; set; }
    public int ReadCount { get; set; } = 1;
    public DateTime ScannedAt { get; set; } = DateTime.Now;
    public DateTime LastSeen { get; set; } = DateTime.Now;

    /// <summary>Unique key: TID when present (chip-unique), otherwise EPC — same as official EPCTID demo.</summary>
    public string UniqueKey => !string.IsNullOrWhiteSpace(Tid) ? Tid : Epc;

    /// <summary>Legacy alias for EPC.</summary>
    public string TagId
    {
        get => Epc;
        set => Epc = value;
    }

    public RfidTag Clone() => new()
    {
        Epc = Epc,
        Tid = Tid,
        User = User,
        TagType = TagType,
        Rssi = Rssi,
        ReadCount = ReadCount,
        ScannedAt = ScannedAt,
        LastSeen = LastSeen
    };
}
