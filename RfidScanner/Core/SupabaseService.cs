using System;
using System.Threading.Tasks;
using Supabase;
using RfidScanner.Models;

namespace RfidScanner.Core;

public class SupabaseService
{
    private const string SUPABASE_URL = "https://caljfmvdqrhcxaunhizi.supabase.co";
    private const string SUPABASE_KEY = "sb_publishable_kjHSzvk03BWZskmycCG35w_KD03h_bi";

    public static SupabaseService Instance { get; } = new();

    public Client Client { get; private set; }

    public event Action<Supabase.Gotrue.Session?>? AuthStateChanged;

    private SupabaseService()
    {
        var options = new SupabaseOptions
        {
            AutoRefreshToken = true,
            AutoConnectRealtime = true
        };
        
        Client = new Client(SUPABASE_URL, SUPABASE_KEY, options);
    }

    public async Task InitializeAsync()
    {
        await Client.InitializeAsync().ConfigureAwait(false);
        Client.Auth.AddStateChangedListener((sender, state) =>
        {
            AuthStateChanged?.Invoke(Client.Auth.CurrentSession);
        });
    }

    public async Task<(bool Success, string ErrorMessage)> LoginAsync(string email, string password)
    {
        try
        {
            var encryptedPassword = AESUtils.Encrypt(password);
            
            var response = await Client.From<RfidScanner.Models.UserModel>()
                .Where(x => x.Email == email && x.Password == encryptedPassword)
                .Get().ConfigureAwait(false);

            var users = response.Models;
            if (users != null && users.Count > 0)
            {
                var user = users.Find(u => !u.IsDeleted) ?? users[0];
                if (!user.IsDeleted && IsUserActive(user))
                {
                    SetCurrentUser(user);
                    return (true, string.Empty);
                }
            }
            return (false, "Invalid email or password.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("LoginAsync Error: " + ex.ToString());
            return (false, "Error: " + ex.Message);
        }
    }

    /// <summary>RFIDStockPro SplashActivity: restore session if logged in previously.</summary>
    public async Task<bool> TryRestoreSessionAsync()
    {
        var stored = SessionManager.LoadSession();
        if (stored == null || !stored.IsLoggedIn || string.IsNullOrWhiteSpace(stored.Email))
            return false;

        try
        {
            var user = await GetUserByEmailAsync(stored.Email).ConfigureAwait(false);
            if (user != null && !user.IsDeleted && IsUserActive(user))
            {
                SetCurrentUser(user);
                return true;
            }

            SessionManager.ClearSession();
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TryRestoreSession online verify failed: {ex.Message}");

            if (stored.CachedUser != null && !stored.CachedUser.IsDeleted)
            {
                SetCurrentUser(stored.CachedUser);
                return true;
            }

            return false;
        }
    }

    public async Task<UserModel?> GetUserByEmailAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        try
        {
            var response = await Client.From<UserModel>()
                .Where(x => x.Email == email)
                .Get().ConfigureAwait(false);

            var users = response.Models;
            if (users == null || users.Count == 0)
                return null;

            return users.Find(u => !u.IsDeleted) ?? users[0];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetUserByEmailAsync failed: {ex.Message}");
            return null;
        }
    }

    private void SetCurrentUser(UserModel user)
    {
        CurrentUserProfile = user;
        SessionManager.SaveSession(user);
        AuthStateChanged?.Invoke(null);
    }

    private static bool IsUserActive(UserModel user)
    {
        return string.IsNullOrWhiteSpace(user.Status)
            || user.Status.Equals("active", StringComparison.OrdinalIgnoreCase)
            || user.Status.Equals("Active", StringComparison.OrdinalIgnoreCase);
    }

    public RfidScanner.Models.UserModel? CurrentUserProfile { get; private set; }

    public async Task LogoutAsync()
    {
        CurrentUserProfile = null;
        SessionManager.ClearSession();
        AuthStateChanged?.Invoke(null);
        await Task.CompletedTask;
    }

    public bool IsAuthenticated => CurrentUserProfile != null;
    public string? CurrentUserEmail => CurrentUserProfile?.Email;

    public async Task<System.Collections.Generic.List<RfidScanner.Models.UserModel>> GetCompanyUsersAsync(string companyName)
    {
        try
        {
            var response = await Client.From<RfidScanner.Models.UserModel>()
                .Where(x => x.CompanyName == companyName && !x.IsDeleted)
                .Get().ConfigureAwait(false);
            return response.Models ?? new System.Collections.Generic.List<RfidScanner.Models.UserModel>();
        }
        catch (Exception)
        {
            return new System.Collections.Generic.List<RfidScanner.Models.UserModel>();
        }
    }
}
