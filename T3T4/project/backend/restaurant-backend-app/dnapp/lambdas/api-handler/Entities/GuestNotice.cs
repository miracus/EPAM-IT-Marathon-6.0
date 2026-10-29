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

    /// <summary>One of <see cref="Constants.GuestNoticeTypes"/>. Null in notices stored before US14/US17.</summary>
    [DynamoDBProperty("type")]
    public string? Type { get; set; }

    /// <summary>Who made the change (the owner's first name).</summary>
    [DynamoDBProperty("actorName")]
    public string? ActorName { get; set; }

    [DynamoDBProperty("dishName")]
    public string? DishName { get; set; }

    [DynamoDBProperty("quantity")]
    public int? Quantity { get; set; }

    /// <summary>Guest's cart total before and after the change (US16 AC1).</summary>
    [DynamoDBProperty("oldTotal")]
    public decimal? OldTotal { get; set; }

    [DynamoDBProperty("newTotal")]
    public decimal? NewTotal { get; set; }

    /// <summary>Amount to pay for payment requests (US17).</summary>
    [DynamoDBProperty("amount")]
    public decimal? Amount { get; set; }
}
