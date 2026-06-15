using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RfidScanner.Models;

namespace RfidScanner.Core;

public class CloudSyncService : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly ConcurrentQueue<RfidTag> _queue = new();
    private CancellationTokenSource? _cts;
    private Task? _workerTask;

    public string ApiEndpoint { get; set; } = "";
    public bool IsSyncEnabled { get; set; }
    
    public event Action<int>? QueueSizeChanged;
    public event Action<string>? SyncErrorOccurred;

    public void Start()
    {
        if (_workerTask != null) return;
        _cts = new CancellationTokenSource();
        _workerTask = Task.Run(() => ProcessQueueAsync(_cts.Token));
    }

    public void EnqueueTag(RfidTag tag)
    {
        if (!IsSyncEnabled || string.IsNullOrWhiteSpace(ApiEndpoint)) return;
        _queue.Enqueue(tag);
        QueueSizeChanged?.Invoke(_queue.Count);
    }

    private async Task ProcessQueueAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var batch = new List<RfidTag>();
            while (_queue.TryDequeue(out var tag) && batch.Count < 50)
            {
                batch.Add(tag);
            }

            if (batch.Count > 0)
            {
                try
                {
                    var payload = new {
                        Timestamp = DateTime.UtcNow,
                        Tags = batch
                    };
                    var json = JsonConvert.SerializeObject(payload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    
                    var response = await _httpClient.PostAsync(ApiEndpoint, content, token);
                    response.EnsureSuccessStatusCode();
                    
                    QueueSizeChanged?.Invoke(_queue.Count);
                }
                catch (Exception ex)
                {
                    SyncErrorOccurred?.Invoke(ex.Message);
                    // On failure, we don't re-queue to avoid memory leaks if endpoint is down forever,
                    // but in a real app you might use a local SQLite buffer or retry policies.
                    await Task.Delay(5000, token);
                }
            }
            else
            {
                await Task.Delay(1000, token);
            }
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _workerTask?.Wait(2000); } catch { }
        _workerTask = null;
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _httpClient.Dispose();
    }
}
