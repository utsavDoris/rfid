using System.Collections.Generic;
using Newtonsoft.Json;

namespace RfidScanner.Models;

public class ProductModel
{
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("productName")]
    public string ProductName { get; set; } = string.Empty;

    [JsonProperty("productCategory")]
    public string ProductCategory { get; set; } = string.Empty;

    [JsonProperty("styleNo")]
    public string StyleNo { get; set; } = string.Empty;

    [JsonProperty("sku")]
    public string Sku { get; set; } = string.Empty;

    [JsonProperty("price")]
    public string Price { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonProperty("tagId")]
    public string TagId { get; set; } = string.Empty;

    [JsonProperty("status")]
    public string Status { get; set; } = "active";

    [JsonProperty("stockStatus")]
    public string StockStatus { get; set; } = "in_stock";

    [JsonProperty("label")]
    public string Label { get; set; } = string.Empty;

    [JsonProperty("location")]
    public string Location { get; set; } = string.Empty;

    [JsonProperty("totalDiaWt")]
    public string? TotalDiaWt { get; set; }

    [JsonProperty("totalGrossWt")]
    public string? TotalGrossWt { get; set; }

    [JsonProperty("totalDiaCount")]
    public string? TotalDiaCount { get; set; }

    [JsonProperty("isDeleted")]
    public bool IsDeleted { get; set; }

    [JsonProperty("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonProperty("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonProperty("selectedImages")]
    public List<string> SelectedImages { get; set; } = new();

    public string StockStatusDisplay => StockStatus switch
    {
        "in_stock" => "In Stock",
        "out_of_stock" => "Out of Stock",
        "sold" => "Sold",
        "memo" => "Memo",
        _ => StockStatus
    };
}
