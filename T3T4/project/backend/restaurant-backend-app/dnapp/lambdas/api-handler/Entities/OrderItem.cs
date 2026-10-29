using Amazon.DynamoDBv2.DataModel;
using APIHandler.Constants.Orders;
using APIHandler.Converters;
using System.Text.Json.Serialization;

namespace APIHandler.Entities;

public class OrderItem
{
    [DynamoDBProperty("dishId")]
    [JsonPropertyName("dishId")]
    public required string DishId { get; set; }

    [DynamoDBProperty("quantity")]
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [DynamoDBProperty("state", typeof(EnumAsIntConverter<OrderItemState>))]
    [JsonPropertyName("state")]
    public OrderItemState State { get; set; }

    /// <summary>Note for the kitchen, up to 80 characters (US18).</summary>
    [DynamoDBProperty("note")]
    [JsonPropertyName("note")]
    public string? Note { get; set; }

    /// <summary>"OnArrival" or "AfterMains"; null means the default for the dish type (US19).</summary>
    [DynamoDBProperty("serveTime")]
    [JsonPropertyName("serveTime")]
    public string? ServeTime { get; set; }
}