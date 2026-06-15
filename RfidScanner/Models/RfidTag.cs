using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RfidScanner.Models;

public class RfidTag : INotifyPropertyChanged
{
    private string _epc = string.Empty;
    private string _tid = string.Empty;
    private string _user = string.Empty;
    private string _tagType = "EPC";
    private int _rssi;
    private string _rssiDisplay = "—";
    private int _readCount = 1;
    private DateTime _scannedAt = DateTime.Now;
    private DateTime _lastSeen = DateTime.Now;

    public string Epc
    {
        get => _epc;
        set => SetField(ref _epc, value);
    }

    public string Tid
    {
        get => _tid;
        set => SetField(ref _tid, value);
    }

    public string User
    {
        get => _user;
        set => SetField(ref _user, value);
    }

    public string TagType
    {
        get => _tagType;
        set => SetField(ref _tagType, value);
    }

    public int Rssi
    {
        get => _rssi;
        set => SetField(ref _rssi, value);
    }

    public string RssiDisplay
    {
        get => _rssiDisplay;
        set => SetField(ref _rssiDisplay, value);
    }

    public int ReadCount
    {
        get => _readCount;
        set => SetField(ref _readCount, value);
    }

    public DateTime ScannedAt
    {
        get => _scannedAt;
        set => SetField(ref _scannedAt, value);
    }

    public DateTime LastSeen
    {
        get => _lastSeen;
        set => SetField(ref _lastSeen, value);
    }

    /// <summary>Unique key: TID when present (chip-unique), otherwise EPC.</summary>
    public string UniqueKey => !string.IsNullOrWhiteSpace(Tid) ? Tid : Epc;

    public string TagId
    {
        get => Epc;
        set => Epc = value;
    }

    /// <summary>Tag column: continuous hex TID (no spaces).</summary>
    public string TidDisplay => string.IsNullOrWhiteSpace(Tid) ? "—" : Tid.Replace(" ", "").Replace("-", "");

    /// <summary>RFIDStockPro-style display: "Tag ID: {TID or EPC}".</summary>
    public string TagIdDisplay
    {
        get
        {
            var id = !string.IsNullOrWhiteSpace(Tid) ? Tid : Epc;
            return string.IsNullOrWhiteSpace(id) ? "Tag ID: —" : $"Tag ID: {id}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RfidTag Clone() => new()
    {
        Epc = Epc,
        Tid = Tid,
        User = User,
        TagType = TagType,
        Rssi = Rssi,
        RssiDisplay = RssiDisplay,
        ReadCount = ReadCount,
        ScannedAt = ScannedAt,
        LastSeen = LastSeen
    };

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName is nameof(Tid) or nameof(Epc))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TagIdDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TidDisplay)));
        }
    }
}
