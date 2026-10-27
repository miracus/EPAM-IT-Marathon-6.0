using APIHandler.Models.Orders;
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
}

public record Participant(
    string? Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ParticipantType>))] ParticipantType Type,
    string Name,
    IEnumerable<OrderItemResponse> Items,
    decimal Total,
    bool IsSubmitted);

/// <summary>One dish across the whole group. Unavailable dishes are listed but not counted in totals.</summary>
public record DishSummary(string DishId, string Name, int Quantity, decimal Total, bool IsAvailable);

public enum ParticipantType
{
    Owner,
    Guest
}
