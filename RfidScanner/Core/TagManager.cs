using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using RfidScanner.Models;

namespace RfidScanner.Core;

public class TagManager
{
    private readonly Dictionary<string, RfidTag> _liveTags = new();
    private readonly object _lock = new();

    public ObservableCollection<RfidTag> LiveTags { get; } = new();
    public event Action<RfidTag>? TagAddedOrUpdated;
    public event Action<RfidTag>? NewTagDiscovered;
    public event Action? LiveTagsChanged;

    public void Start()
    {
        // Tags persist until the user clicks Clear.
    }

    public void Stop()
    {
    }

    public RfidTag ProcessTag(RfidTag incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming.Tid))
            return incoming;

        var key = incoming.Tid;
        if (string.IsNullOrWhiteSpace(key))
            return incoming;

        RfidTag result;
        bool isNew = false;

        lock (_lock)
        {
            if (_liveTags.TryGetValue(key, out var existing))
            {
                existing.ReadCount++;
                existing.Rssi = incoming.Rssi;
                existing.LastSeen = DateTime.Now;
                if (!string.IsNullOrWhiteSpace(incoming.Epc))
                    existing.Epc = incoming.Epc;
                if (!string.IsNullOrWhiteSpace(incoming.Tid))
                    existing.Tid = incoming.Tid;
                if (!string.IsNullOrWhiteSpace(incoming.User))
                    existing.User = incoming.User;
                existing.TagType = RfidTagMapper.ResolveTagType(existing.Epc, existing.Tid, existing.User);
                existing.Rssi = incoming.Rssi;
                existing.RssiDisplay = incoming.RssiDisplay;
                result = existing.Clone();
            }
            else
            {
                var tag = incoming.Clone();
                tag.ScannedAt = DateTime.Now;
                tag.LastSeen = DateTime.Now;
                tag.ReadCount = 1;

                _liveTags[key] = tag;
                RunOnUi(() => LiveTags.Insert(0, tag));
                result = tag.Clone();
                isNew = true;
            }
        }

        if (isNew) NewTagDiscovered?.Invoke(result);
        TagAddedOrUpdated?.Invoke(result);
        return result;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _liveTags.Clear();
            RunOnUi(() =>
            {
                LiveTags.Clear();
                LiveTagsChanged?.Invoke();
            });
        }
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }
}
