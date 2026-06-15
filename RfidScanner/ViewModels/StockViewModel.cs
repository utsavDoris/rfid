using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RfidScanner.Core;
using RfidScanner.Models;

namespace RfidScanner.ViewModels;

public partial class StockViewModel : ObservableObject
{
    private readonly ProductService _productService = new();

    [ObservableProperty]
    private string _statusMessage = "Loading products...";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public ObservableCollection<ProductModel> Products { get; } = new();
    public ICollectionView ProductsView { get; }

    public StockViewModel()
    {
        ProductsView = CollectionViewSource.GetDefaultView(Products);
        ProductsView.Filter = FilterProduct;
        _ = LoadProductsAsync();
    }

    partial void OnSearchTextChanged(string value) => ProductsView.Refresh();

    [RelayCommand]
    private async Task RefreshAsync() => await LoadProductsAsync();

    private async Task LoadProductsAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading products from cloud...";

        try
        {
            var company = SupabaseService.Instance.CurrentUserProfile?.CompanyName ?? string.Empty;
            var (products, error) = await _productService.GetProductsAsync(company).ConfigureAwait(true);

            Products.Clear();
            foreach (var p in products)
                Products.Add(p);

            StatusMessage = error != null
                ? $"Error: {error}"
                : $"{Products.Count} products loaded.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool FilterProduct(object obj)
    {
        if (obj is not ProductModel product) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        var text = SearchText.Trim().ToLowerInvariant();
        return (product.Sku?.ToLowerInvariant().Contains(text) ?? false)
            || (product.ProductName?.ToLowerInvariant().Contains(text) ?? false)
            || (product.TagId?.ToLowerInvariant().Contains(text) ?? false)
            || (product.StyleNo?.ToLowerInvariant().Contains(text) ?? false);
    }
}
