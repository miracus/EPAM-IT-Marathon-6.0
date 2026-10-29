using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace APIHandler.Models.GuestSessions;

/// <summary>US18/US19: note and serving time of one dish. Null fields are left unchanged; an empty note clears it.</summary>
public class DishDetailsRequest
{
    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("serveTime")]
    public string? ServeTime { get; set; }
}

/// <summary>US18: allergies or dietary needs. Empty clears them.</summary>
public class AllergiesRequest
{
    [JsonPropertyName("allergies")]
    public string? Allergies { get; set; }
}

/// <summary>US15 Undo: put a removed dish back with the same quantity, note and serving time.</summary>
public class RestoreDishRequest
{
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("serveTime")]
    public string? ServeTime { get; set; }
}

/// <summary>US14: remind the given guests, or everyone who has not chosen yet when empty.</summary>
public class RemindRequest
{
    [JsonPropertyName("guestSessionIds")]
    public List<string>? GuestSessionIds { get; set; }
}

public record ReminderResult(string Id, string Name, DateTime? LastRemindedAt);

/// <summary>US14: who got a reminder and who was skipped because of the 15-minute limit.</summary>
public record RemindersResponse(IReadOnlyList<ReminderResult> Reminded, IReadOnlyList<ReminderResult> Skipped);

/// <summary>US14 AC3: group settings changed by the owner.</summary>
public class GroupSettingsRequest
{
    [JsonPropertyName("autoRemind")]
    public bool? AutoRemind { get; set; }
}

/// <summary>US17: payment mode chosen by the owner.</summary>
public class PaymentModeRequest
{
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }
}

public record PaymentParticipant(
    string Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ParticipantType>))] ParticipantType Type,
    string Name,
    decimal OwnDishesTotal,
    decimal Amount,
    bool IsPaid,
    DateTime? PaidAt,
    DateTime? LinkSentAt);

/// <summary>US17: shares of every participant, Paid $X · Pending $Y with X + Y = Grand Total.</summary>
public record PaymentView(
    string? Mode,
    IReadOnlyList<PaymentParticipant> Participants,
    decimal GrandTotal,
    decimal PaidTotal,
    decimal PendingTotal);

/// <summary>US21: the new group linked to the previous one.</summary>
public record RebookResponse(string GroupReservationId, string InviteToken, int InvitedParticipants);
