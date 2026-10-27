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

/// <summary>A change made by the owner that the guest should see (US16).</summary>
public record GuestNoticeResponse(string Id, string Message, string? DishId, DateTime CreatedAt);
