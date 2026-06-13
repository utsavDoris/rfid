using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using RfidScanner.Models;

namespace RfidScanner.Data;

public class ScanDatabase : IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _connection;
    private bool _disposed;

    public ScanDatabase()
    {
        _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scans.db");
        Initialize();
    }

    private void Initialize()
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Scans (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TagId TEXT NOT NULL,
                TagType TEXT NOT NULL,
                Rssi INTEGER NOT NULL,
                ReadCount INTEGER NOT NULL,
                ScannedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Scans_ScannedAt ON Scans(ScannedAt DESC);
            """;
        cmd.ExecuteNonQuery();
    }

    public void SaveScan(RfidTag tag)
    {
        if (_connection == null) return;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Scans (TagId, TagType, Rssi, ReadCount, ScannedAt)
            VALUES ($tagId, $tagType, $rssi, $readCount, $scannedAt);
            """;
        cmd.Parameters.AddWithValue("$tagId", tag.TagId);
        cmd.Parameters.AddWithValue("$tagType", tag.TagType);
        cmd.Parameters.AddWithValue("$rssi", tag.Rssi);
        cmd.Parameters.AddWithValue("$readCount", tag.ReadCount);
        cmd.Parameters.AddWithValue("$scannedAt", tag.ScannedAt.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public List<RfidTag> GetHistory(int limit = 500)
    {
        var results = new List<RfidTag>();
        if (_connection == null) return results;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT TagId, TagType, Rssi, ReadCount, ScannedAt
            FROM Scans
            ORDER BY ScannedAt DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new RfidTag
            {
                TagId = reader.GetString(0),
                TagType = reader.GetString(1),
                Rssi = reader.GetInt32(2),
                ReadCount = reader.GetInt32(3),
                ScannedAt = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            });
        }

        return results;
    }

    public string ExportCsv(string? filePath = null)
    {
        filePath ??= Path.Combine(
            Directory.GetCurrentDirectory(),
            $"rfid_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        var tags = GetHistory(10000);
        var sb = new StringBuilder();
        sb.AppendLine("TagId,TagType,Rssi,ReadCount,ScannedAt");

        foreach (var tag in tags)
        {
            sb.AppendLine(string.Join(",",
                EscapeCsv(tag.TagId),
                EscapeCsv(tag.TagType),
                tag.Rssi.ToString(CultureInfo.InvariantCulture),
                tag.ReadCount.ToString(CultureInfo.InvariantCulture),
                tag.ScannedAt.ToString("o", CultureInfo.InvariantCulture)));
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        return filePath;
    }

    public void ClearHistory()
    {
        if (_connection == null) return;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Scans;";
        cmd.ExecuteNonQuery();
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection?.Dispose();
        _connection = null;
    }
}
