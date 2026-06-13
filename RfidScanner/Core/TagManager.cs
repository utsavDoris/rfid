using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using RfidScanner.Models;

namespace RfidScanner.Core;

public class TagManager
{
    private readonly Dictionary<string, RfidTag> _liveTags = new();
    private readonly object _lock = new();
    private Timer? _purgeTimer;

    public ObservableCollection<RfidTag> LiveTags { get; } = new();
    public event Action<RfidTag>? TagAddedOrUpdated;

    public void Start()
    {
        _purgeTimer ??= new Timer(PurgeStaleTags, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public void Stop()
    {
        _purgeTimer?.Dispose();
        _purgeTimer = null;
    }

    public RfidTag ProcessTag(RfidTag incoming)
    {
        RfidTag result;

        lock (_lock)
        {
            if (_liveTags.TryGetValue(incoming.TagId, out var existing))
            {
                existing.ReadCount++;
                existing.Rssi = incoming.Rssi;
                existing.LastSeen = DateTime.Now;
                result = existing.Clone();
            }
            else
            {
                var tag = incoming.Clone();
                tag.ScannedAt = DateTime.Now;
                tag.LastSeen = DateTime.Now;
                tag.ReadCount = 1;

                _liveTags[tag.TagId] = tag;
                RunOnUi(() => LiveTags.Insert(0, tag));
                result = tag.Clone();
            }
        }

        TagAddedOrUpdated?.Invoke(result);
        return result;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _liveTags.Clear();
            RunOnUi(() => LiveTags.Clear());
        }
    }

    private void PurgeStaleTags(object? state)
    {
        var cutoff = DateTime.Now.AddSeconds(-30);
        List<RfidTag>? toRemove = null;

        lock (_lock)
        {
            toRemove = _liveTags.Values.Where(t => t.LastSeen < cutoff).ToList();
            foreach (var tag in toRemove)
                _liveTags.Remove(tag.TagId);
        }

        if (toRemove == null || toRemove.Count == 0)
            return;

        RunOnUi(() =>
        {
            foreach (var tag in toRemove)
            {
                var item = LiveTags.FirstOrDefault(t => t.TagId == tag.TagId);
                if (item != null)
                    LiveTags.Remove(item);
            }
        });
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }
}
