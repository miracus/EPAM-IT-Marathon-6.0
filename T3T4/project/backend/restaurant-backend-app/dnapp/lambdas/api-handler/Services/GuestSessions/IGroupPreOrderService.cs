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

    /// <summary>US15 AC2: Undo — put a removed dish back.</summary>
    Task<GetAllSessionsResponse> RestoreGuestDishAsync(string userId, string groupReservationId, string guestSessionId, string dishId, RestoreDishRequest request);

    /// <summary>US15 AC4: "Ask to change order" — the guest can edit and submit again.</summary>
    Task<GetAllSessionsResponse> RequestChangeAsync(string userId, string groupReservationId, string guestSessionId);

    /// <summary>US14: remind guests who have not chosen yet (or, at the payment step, who have not paid).</summary>
    Task<RemindersResponse> SendRemindersAsync(string userId, string groupReservationId, RemindRequest request);

    /// <summary>US14 AC3: auto-remind on/off.</summary>
    Task<GetAllSessionsResponse> UpdateSettingsAsync(string userId, string groupReservationId, GroupSettingsRequest request);

    /// <summary>US18/US19: note and serving time of a dish in the owner's own pre-order.</summary>
    Task<GetAllSessionsResponse> UpdateOwnerDishAsync(string userId, string groupReservationId, string dishId, DishDetailsRequest request);

    /// <summary>US18: the owner's own allergies.</summary>
    Task<GetAllSessionsResponse> UpdateOwnerAllergiesAsync(string userId, string groupReservationId, AllergiesRequest request);

    /// <summary>US17: shares and payment status.</summary>
    Task<PaymentView> GetPaymentAsync(string userId, string groupReservationId);

    Task<PaymentView> SetPaymentModeAsync(string userId, string groupReservationId, string? mode);

    Task<PaymentView> SendPaymentLinksAsync(string userId, string groupReservationId);

    Task<PaymentView> MarkPaidAsync(string userId, string groupReservationId, string guestSessionId);

    /// <summary>US17: the share of one guest (no owner check — called with a valid guest token).</summary>
    Task<GuestPaymentShare?> GetGuestPaymentShareAsync(string groupReservationId, string guestSessionId);

    Task<GuestPaymentShare> PayGuestShareAsync(string groupReservationId, string guestSessionId);

    /// <summary>US21: link a new reservation to a previous one so the old group gets the new invite.</summary>
    Task<RebookResponse> RebookAsync(string userId, string reservationId, string previousReservationId);

    /// <summary>US13: send the pre-order automatically once the cut-off has passed; US14 AC3 auto-remind.</summary>
    Task EnforceCutoffAsync(string groupReservationId);
}
