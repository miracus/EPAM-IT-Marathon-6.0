using Amazon.DynamoDBv2.DataModel;
using System;

namespace APIHandler.Entities;

/// <summary>
/// A message for a guest about a change the reservation owner made to the guest's order (US16).
/// Stored inside the guest session; cleared when the guest acknowledges it.
/// </summary>
public class GuestNotice
{
    [DynamoDBProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [DynamoDBProperty("message")]
    public string Message { get; set; } = string.Empty;

    [DynamoDBProperty("dishId")]
    public string? DishId { get; set; }

    [DynamoDBProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
