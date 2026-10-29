using Amazon.DynamoDBv2.DataModel;
using APIHandler.Constants;
using System;
using System.Collections.Generic;

namespace APIHandler.Entities;

public class GuestSession
{
    [DynamoDBHashKey("guestSessionId")]
    public required string Id { get; set; }

    [DynamoDBProperty("groupReservationId")]
    [DynamoDBGlobalSecondaryIndexHashKey(GlobalSecondaryIndexes.GuestSession.GroupReservationId)]
    public string GroupReservationId { get; set; } = string.Empty;

    [DynamoDBProperty("guestName")]
    public required string GuestName { get; set; }

    [DynamoDBProperty("dishes")]
    public required List<OrderItem> Dishes { get; set; }

    [DynamoDBProperty("guestSessionTokenHash")]
    [DynamoDBGlobalSecondaryIndexHashKey(GlobalSecondaryIndexes.GuestSession.SessionTokenHash)]
    public string? SessionTokenHash { get; set; }

    [DynamoDBProperty("isOwnerSession")]
    public bool IsOwnerSession { get; set; }

    [DynamoDBProperty("ownerUserId")]
    public string? OwnerUserId { get; set; }

    [DynamoDBProperty("isSubmitted")]
    public bool IsSubmitted { get; set; }

    /// <summary>Changes made by the reservation owner that the guest has not acknowledged yet (US16).</summary>
    [DynamoDBProperty("notices")]
    public List<GuestNotice>? Notices { get; set; }

    /// <summary>Allergies or dietary needs stated by the participant (US18).</summary>
    [DynamoDBProperty("allergies")]
    public string? Allergies { get; set; }

    /// <summary>The owner changed this guest's order (US15 "Changed by you").</summary>
    [DynamoDBProperty("changedByOwner")]
    public bool ChangedByOwner { get; set; }

    /// <summary>Last manual or automatic reminder (US14, one per 15 minutes).</summary>
    [DynamoDBProperty("lastRemindedAt")]
    public DateTime? LastRemindedAt { get; set; }

    /// <summary>Payment status of the participant's share (US17, simulated).</summary>
    [DynamoDBProperty("isPaid")]
    public bool IsPaid { get; set; }

    [DynamoDBProperty("paidAt")]
    public DateTime? PaidAt { get; set; }

    [DynamoDBProperty("paymentLinkSentAt")]
    public DateTime? PaymentLinkSentAt { get; set; }
}