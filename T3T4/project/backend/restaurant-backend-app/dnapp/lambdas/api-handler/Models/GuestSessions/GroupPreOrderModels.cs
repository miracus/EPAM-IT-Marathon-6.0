using System;
using System.ComponentModel.DataAnnotations;

namespace APIHandler.Models.GuestSessions;

/// <summary>Response of GET /reservations/{reservationId}/guest-session-id (homework L2).</summary>
public record OwnerSessionIdResponse(string GuestSessionId);

/// <summary>Body of POST /group-reservations/{groupReservationId}/submit (homework L2).</summary>
public class SubmitGroupPreOrderRequest
{
    [Required]
    public string ReservationId { get; set; } = string.Empty;
}

/// <summary>One merged dish line returned by the group submit (homework L2).</summary>
public record SubmittedDishResponse(string DishId, int Quantity);

/// <summary>A message for the guest: a change made by the owner (US16), a reminder (US14) or a payment request (US17).</summary>
public record GuestNoticeResponse(string Id, string Message, string? DishId, DateTime CreatedAt)
{
    public string? Type { get; init; }
    public string? ActorName { get; init; }
    public string? DishName { get; init; }
    public int? Quantity { get; init; }
    public decimal? OldTotal { get; init; }
    public decimal? NewTotal { get; init; }
    public decimal? Amount { get; init; }
}
