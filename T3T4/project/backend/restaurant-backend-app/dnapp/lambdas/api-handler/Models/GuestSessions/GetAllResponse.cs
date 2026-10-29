using APIHandler.Models.Orders;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace APIHandler.Models.GuestSessions;

/// <summary>
/// Snapshot of a group pre-order for the reservation owner (homework L1/L3, US12).
/// <c>participants</c> and <c>total</c> follow the homework contract; the init-only
/// properties are backward-compatible extensions from US12 (summary by dish, unavailable dishes).
/// </summary>
public record GetAllSessionsResponse(IEnumerable<Participant> Participants, decimal Total)
{
    /// <summary>Identical dishes across all participants, grouped (US12 AC5 — "Summary by dish").</summary>
    public IEnumerable<DishSummary> SummaryByDish { get; init; } = [];

    /// <summary>True when at least one selected dish is no longer available (US12 AC6).</summary>
    public bool HasUnavailableDishes { get; init; }

    /// <summary>Open / Submitted / Closed.</summary>
    public string? GroupStatus { get; init; }

    /// <summary>US13: when changes close (same time representation as the reservation start time).</summary>
    public DateTime? CutoffAt { get; init; }

    /// <summary>US13: seconds left until the cut-off, computed on the server (0 when passed).</summary>
    public long SecondsUntilCutoff { get; init; }

    /// <summary>US13 AC3: true after submit or after the cut-off — no more changes.</summary>
    public bool IsLocked { get; init; }

    /// <summary>US13 AC2/AC5: when the pre-order was sent and whether it was sent automatically.</summary>
    public DateTime? SubmittedAt { get; init; }

    public bool SubmittedAutomatically { get; init; }

    /// <summary>US14 AC3.</summary>
    public bool AutoRemind { get; init; }

    /// <summary>US17: chosen payment mode, null until chosen.</summary>
    public string? PaymentMode { get; init; }

    /// <summary>Guest count of the reservation (US20 "4 of 5 joined").</summary>
    public int MaxGuests { get; init; }

    /// <summary>Reservation status: Reserved / Cancelled / InProgress / Completed (US21).</summary>
    public string? ReservationStatus { get; init; }
}

public record Participant(
    string? Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ParticipantType>))] ParticipantType Type,
    string Name,
    IEnumerable<OrderItemResponse> Items,
    decimal Total,
    bool IsSubmitted)
{
    /// <summary>US18 AC2: allergies or dietary needs, shown as a red chip.</summary>
    public string? Allergies { get; init; }

    /// <summary>US15 AC3: "Changed by you".</summary>
    public bool ChangedByOwner { get; init; }

    /// <summary>US14 AC4: last reminder time.</summary>
    public DateTime? LastRemindedAt { get; init; }

    /// <summary>US17: payment status of the participant's share.</summary>
    public bool IsPaid { get; init; }
}

/// <summary>One dish across the whole group. Unavailable dishes are listed but not counted in totals.</summary>
public record DishSummary(string DishId, string Name, int Quantity, decimal Total, bool IsAvailable);

public enum ParticipantType
{
    Owner,
    Guest
}
