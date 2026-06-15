namespace RfidScanner;

/// <summary>
/// Coordinates login → main → logout → login flow.
/// </summary>
internal static class AppSession
{
    public static bool ReturnToLogin { get; set; }
}
