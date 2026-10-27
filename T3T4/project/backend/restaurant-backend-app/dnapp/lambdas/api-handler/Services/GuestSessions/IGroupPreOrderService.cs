using APIHandler.Models.GuestSessions;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace APIHandler.Services.GuestSessions;

/// <summary>
/// Owner-side operations on a group pre-order.
/// Every method checks that the caller (Cognito user id) owns the reservation behind the group.
/// </summary>
public interface IGroupPreOrderService
{
    /// <summary>Homework L1 / US12: participants, their dishes and totals.</summary>
    Task<GetAllSessionsResponse> GetSessionsAsync(string userId, string groupReservationId);

    /// <summary>Homework L2: the owner's own guest session for a reservation (created on first request).</summary>
    Task<OwnerSessionIdResponse> GetOwnerSessionIdAsync(string userId, string reservationId);

    /// <summary>Homework L2 / US13: merge submitted sessions into the order and lock the group.</summary>
    Task<IReadOnlyList<SubmittedDishResponse>> SubmitAsync(string userId, string groupReservationId, string reservationId);

    /// <summary>Homework L3: remove a participant and return the updated snapshot.</summary>
    Task<GetAllSessionsResponse> DeleteSessionAsync(string userId, string groupReservationId, string guestSessionId);

    /// <summary>US15 + US16: remove one dish from a guest's order and leave the guest a notice.</summary>
    Task<GetAllSessionsResponse> RemoveGuestDishAsync(string userId, string groupReservationId, string guestSessionId, string dishId);
}
