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
                Epc TEXT NOT NULL DEFAULT '',
                Tid TEXT NOT NULL DEFAULT '',
                TagType TEXT NOT NULL,
                Rssi INTEGER NOT NULL,
                ReadCount INTEGER NOT NULL,
                ScannedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Scans_ScannedAt ON Scans(ScannedAt DESC);
            """;
        cmd.ExecuteNonQuery();

        EnsureColumn("Epc", "ALTER TABLE Scans ADD COLUMN Epc TEXT NOT NULL DEFAULT '';");
        EnsureColumn("Tid", "ALTER TABLE Scans ADD COLUMN Tid TEXT NOT NULL DEFAULT '';");
    }

    private void EnsureColumn(string columnName, string alterSql)
    {
        if (_connection == null) return;

        using var check = _connection.CreateCommand();
        check.CommandText = "SELECT 1 FROM pragma_table_info('Scans') WHERE name = $name;";
        check.Parameters.AddWithValue("$name", columnName);
        if (check.ExecuteScalar() != null)
            return;

        using var alter = _connection.CreateCommand();
        alter.CommandText = alterSql;
        alter.ExecuteNonQuery();
    }

    public void SaveScan(RfidTag tag)
    {
        if (_connection == null) return;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Scans (TagId, Epc, Tid, TagType, Rssi, ReadCount, ScannedAt)
            VALUES ($tagId, $epc, $tid, $tagType, $rssi, $readCount, $scannedAt);
            """;
        cmd.Parameters.AddWithValue("$tagId", tag.UniqueKey);
        cmd.Parameters.AddWithValue("$epc", tag.Epc);
        cmd.Parameters.AddWithValue("$tid", tag.Tid);
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
            SELECT TagId, Epc, Tid, TagType, Rssi, ReadCount, ScannedAt
            FROM Scans
            ORDER BY ScannedAt DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var epc = reader.FieldCount > 2 && !reader.IsDBNull(1) ? reader.GetString(1) : string.Empty;
            var tid = reader.FieldCount > 2 && !reader.IsDBNull(2) ? reader.GetString(2) : string.Empty;

            if (string.IsNullOrWhiteSpace(epc) && !reader.IsDBNull(0))
                epc = reader.GetString(0);

            results.Add(new RfidTag
            {
                Epc = epc,
                Tid = tid,
                TagType = reader.GetString(3),
                Rssi = reader.GetInt32(4),
                ReadCount = reader.GetInt32(5),
                ScannedAt = DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
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
        sb.AppendLine("Epc,Tid,TagType,Rssi,ReadCount,ScannedAt");

        foreach (var tag in tags)
        {
            sb.AppendLine(string.Join(",",
                EscapeCsv(tag.Epc),
                EscapeCsv(tag.Tid),
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
