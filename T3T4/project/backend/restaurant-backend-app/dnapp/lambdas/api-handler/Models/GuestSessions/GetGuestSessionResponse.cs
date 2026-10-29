using APIHandler.Models.Orders;
using System;
using System.Collections.Generic;

namespace APIHandler.Models.GuestSessions;

public record GetGuestSessionResponse(
    string Name,
    IEnumerable<OrderItemResponse> Items,
    decimal Total,
    bool IsSubmitted)
{
    /// <summary>Unacknowledged changes the reservation owner made to this guest's order (US16).</summary>
    public IEnumerable<GuestNoticeResponse> Notices { get; init; } = [];

    /// <summary>US18: allergies or dietary needs.</summary>
    public string? Allergies { get; init; }

    /// <summary>Open / Submitted / Closed.</summary>
    public string? GroupStatus { get; init; }

    /// <summary>US13: cut-off and lock state for the guest screen.</summary>
    public DateTime? CutoffAt { get; init; }

    public long SecondsUntilCutoff { get; init; }

    public bool IsLocked { get; init; }

    /// <summary>The organizer's first name (US16 "Oleh removed …").</summary>
    public string? OwnerName { get; init; }

    /// <summary>US17: the guest's share once the owner has chosen how the group pays.</summary>
    public GuestPaymentShare? Payment { get; init; }

    /// <summary>US21: the organizer booked again with the same group — link to join the new pre-order.</summary>
    public RebookInfo? Rebook { get; init; }
}

public record GuestPaymentShare(string Mode, decimal Amount, bool IsPaid, DateTime? LinkSentAt);

public record RebookInfo(string InviteToken, DateTime StartTime, DateTime EndTime);
