namespace Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrder;

/// <summary>GET /reservations/{reservationId}/invite-token.</summary>
public sealed record InviteTokenResponse(string Token);

/// <summary>GET /guest/invites/{token}: what a guest sees before joining.</summary>
public sealed record GroupInviteResponse(string GroupReservationId, string? LocationAddress, DateTime? StartTime);

/// <summary>POST /guest/sessions: the guest's session and the token that stands in for a sign-in.</summary>
public sealed record GuestSessionCreatedResponse(string SessionId, string SessionToken);

/// <summary>GET /reservations/{reservationId}/guest-session-id: the organizer's own session in the group.</summary>
public sealed record OwnerSessionIdResponse(string GuestSessionId);

/// <summary>One line of the pre-order the organizer sent (POST /group-reservations/{id}/submit).</summary>
public sealed record SubmittedDishResponse(string DishId, int Quantity);

/// <summary>GET /guest/groups/{groupReservationId}/sessions (and the snapshot every owner action returns).</summary>
public sealed record GroupSnapshotResponse(
    IReadOnlyList<GroupParticipantResponse> Participants,
    decimal Total,
    string? GroupStatus);

public sealed record GroupParticipantResponse(
    string Id,
    string Type,
    string Name,
    IReadOnlyList<GroupParticipantItemResponse> Items,
    decimal Total,
    bool IsSubmitted);

public sealed record GroupParticipantItemResponse(GroupDishResponse Dish, int Quantity);

public sealed record GroupDishResponse(string Id, string Name, decimal Price);

/// <summary>GET /guest/groups/{groupReservationId}/status.</summary>
public sealed record GroupStatusResponse(string Status);
