using APIHandler.Models.Dishes;
using System.Text.Json.Serialization;

namespace APIHandler.Models.Orders;

public class OrderItemResponse
{
    [JsonPropertyName("dish")]
    public required DishResponse Dish { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    /// <summary>Participant's note for the kitchen (group pre-order, US18).</summary>
    [JsonPropertyName("note")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; set; }

    /// <summary>Effective serving time "OnArrival" / "AfterMains" (group pre-order, US19).</summary>
    [JsonPropertyName("serveTime")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ServeTime { get; set; }
}
