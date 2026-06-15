using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RfidScanner.Models;

namespace RfidScanner.Core;

public class ProductService
{
    private const string SupabaseUrl = "https://caljfmvdqrhcxaunhizi.supabase.co";
    private const string SupabaseKey = "sb_publishable_kjHSzvk03BWZskmycCG35w_KD03h_bi";

    private static readonly HttpClient Http = new HttpClient();

    public async Task<(List<ProductModel> Products, string? Error)> GetProductsAsync(string companyName)
    {
        if (string.IsNullOrWhiteSpace(companyName))
            return (new List<ProductModel>(), "Company name is missing.");

        var tableName = CompanyTableHelper.GetProductTableName(companyName);
        if (string.IsNullOrEmpty(tableName) || tableName == "_product")
            return (new List<ProductModel>(), "Invalid company table name.");

        try
        {
            var url = $"{SupabaseUrl}/rest/v1/{tableName}?select=*&isDeleted=eq.false&order=updatedAt.desc";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("apikey", SupabaseKey);
            request.Headers.Add("Authorization", $"Bearer {SupabaseKey}");

            var response = await Http.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return (new List<ProductModel>(), $"Could not load products ({(int)response.StatusCode}).");

            var products = JsonConvert.DeserializeObject<List<ProductModel>>(body) ?? new List<ProductModel>();
            return (products, null);
        }
        catch (Exception ex)
        {
            return (new List<ProductModel>(), ex.Message);
        }
    }
}
