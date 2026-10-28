using Amazon.DynamoDBv2.DataModel;
using System.Text.Json.Serialization;

namespace APIHandler.Entities;

/// <summary>A dish line of a group pre-order with the participant's note or serving time (US18, US19).</summary>
public class KitchenNote
{
    [DynamoDBProperty("dishId")]
    [JsonPropertyName("dishId")]
    public string DishId { get; set; } = string.Empty;

    [DynamoDBProperty("dishName")]
    [JsonPropertyName("dishName")]
    public string DishName { get; set; } = string.Empty;

    [DynamoDBProperty("participantName")]
    [JsonPropertyName("participantName")]
    public string ParticipantName { get; set; } = string.Empty;

    [DynamoDBProperty("quantity")]
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [DynamoDBProperty("note")]
    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [DynamoDBProperty("serveTime")]
    [JsonPropertyName("serveTime")]
    public string ServeTime { get; set; } = string.Empty;
}

/// <summary>Allergies or dietary needs of one participant (US18).</summary>
public class ParticipantAllergy
{
    [DynamoDBProperty("participantName")]
    [JsonPropertyName("participantName")]
    public string ParticipantName { get; set; } = string.Empty;

    [DynamoDBProperty("allergies")]
    [JsonPropertyName("allergies")]
    public string Allergies { get; set; } = string.Empty;
}
