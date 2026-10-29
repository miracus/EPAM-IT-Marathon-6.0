using Amazon.DynamoDBv2.DataModel;
using System;
using System.Collections.Generic;

namespace APIHandler.Entities;

public class GroupReservation
{
    [DynamoDBHashKey("groupReservationId")]
    public required string GroupReservationId { get; set; }

    [DynamoDBProperty("reservationId")]
    [DynamoDBGlobalSecondaryIndexHashKey("reservationId")]
    public required string ReservationId { get; set; }

    [DynamoDBProperty("reservationToken")]
    [DynamoDBGlobalSecondaryIndexHashKey("reservationToken")]
    public required string ReservationToken { get; set; }

    [DynamoDBProperty("status")]
    public GroupReservationStatus Status { get; set; }

    /// <summary>When the pre-order was sent to the restaurant (US13 AC2).</summary>
    [DynamoDBProperty("submittedAt")]
    public DateTime? SubmittedAt { get; set; }

    /// <summary>True when the pre-order was sent automatically at the cut-off (US13 AC5).</summary>
    [DynamoDBProperty("submittedAutomatically")]
    public bool SubmittedAutomatically { get; set; }

    /// <summary>Auto-remind is on by default (US14 AC3), so the stored flag is the opposite.</summary>
    [DynamoDBProperty("autoRemindDisabled")]
    public bool AutoRemindDisabled { get; set; }

    [DynamoDBProperty("autoRemindedAt")]
    public DateTime? AutoRemindedAt { get; set; }

    /// <summary>One of <see cref="Constants.PaymentModes"/>; null until the owner chooses (US17).</summary>
    [DynamoDBProperty("paymentMode")]
    public string? PaymentMode { get; set; }

    /// <summary>Guests removed by the owner, so their link can say so (US16 AC5).</summary>
    [DynamoDBProperty("removedSessionIds")]
    public List<string>? RemovedSessionIds { get; set; }

    /// <summary>US21: the reservation this group was booked again from, and the one booked after it.</summary>
    [DynamoDBProperty("previousReservationId")]
    public string? PreviousReservationId { get; set; }

    [DynamoDBProperty("nextReservationId")]
    public string? NextReservationId { get; set; }
}