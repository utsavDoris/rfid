using System;
using System.Threading.Tasks;
using Supabase;

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
                if (!user.IsDeleted)
                {
                    CurrentUserProfile = user;
                    AuthStateChanged?.Invoke(null); // Notify that auth state changed
                    return (true, string.Empty);
                }
            }
            // If empty, return false
            return (false, "Invalid email or password.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("LoginAsync Error: " + ex.ToString());
            return (false, "Error: " + ex.Message);
        }
    }

    public RfidScanner.Models.UserModel? CurrentUserProfile { get; private set; }

    public async Task LogoutAsync()
    {
        CurrentUserProfile = null;
        AuthStateChanged?.Invoke(null);
        await Task.CompletedTask; // Keep signature async
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
