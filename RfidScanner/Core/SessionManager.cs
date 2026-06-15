using System;
using System.IO;
using Newtonsoft.Json;
using RfidScanner.Models;

namespace RfidScanner.Core;

/// <summary>
/// Persists login session like RFIDStockPro SessionManager (SharedPreferences).
/// One-time login: app reopens directly to main shell until user logs out.
/// </summary>
public static class SessionManager
{
    private static readonly string SessionDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AcurisDesktop");

    private static readonly string SessionFilePath = Path.Combine(SessionDirectory, "session.json");

    public static void SaveSession(UserModel user)
    {
        var stored = new StoredSession
        {
            IsLoggedIn = true,
            Email = user.Email ?? string.Empty,
            UserId = user.Id,
            UserName = user.UserName,
            CompanyName = user.CompanyName,
            Role = user.Role,
            CachedUser = user
        };

        Write(stored);
    }

    public static StoredSession? LoadSession()
    {
        try
        {
            if (!File.Exists(SessionFilePath))
                return null;

            var json = File.ReadAllText(SessionFilePath);
            return JsonConvert.DeserializeObject<StoredSession>(json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SessionManager.LoadSession failed: {ex.Message}");
            return null;
        }
    }

    public static bool HasActiveSession()
    {
        var session = LoadSession();
        return session is { IsLoggedIn: true } && !string.IsNullOrWhiteSpace(session.Email);
    }

    public static void ClearSession()
    {
        try
        {
            if (File.Exists(SessionFilePath))
                File.Delete(SessionFilePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SessionManager.ClearSession failed: {ex.Message}");
        }
    }

    private static void Write(StoredSession session)
    {
        try
        {
            Directory.CreateDirectory(SessionDirectory);
            var json = JsonConvert.SerializeObject(session, Formatting.Indented);
            File.WriteAllText(SessionFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SessionManager.Write failed: {ex.Message}");
        }
    }
}

public sealed class StoredSession
{
    public bool IsLoggedIn { get; set; }
    public string Email { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public int Role { get; set; }
    public UserModel? CachedUser { get; set; }
}
