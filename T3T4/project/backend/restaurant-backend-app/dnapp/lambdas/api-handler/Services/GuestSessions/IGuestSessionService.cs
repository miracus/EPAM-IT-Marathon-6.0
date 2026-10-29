using APIHandler.Models.GuestSessions;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace APIHandler.Services.GuestSessions;

public interface IGuestSessionService
{
    Task<CreatGuestSessionResponse> CreateGuestSessionAsync(CreateGuestSessionRequest request);
    Task AddDishAsync(string guestSessionId, string dishId, int quantity);
    Task RemoveDishAsync(string guestSessionId, string dishId, int quantity);
    Task<GroupReservationStatusResponse> GetGroupReservationStatusAsync(string groupReservationId, string? guestSessionId = null);
    Task<bool> IsAuthorizedAsync(string guestSessionToken, string guestSessionId);
    Task SubmitCartAsync(string guestSessionId);
    Task<GuestMenuResponse> GetGuestMenuAsync(string guestSessionId);
    Task AcknowledgeNoticesAsync(string guestSessionId);
    Task<GetGuestSessionResponse> GetGuestSessionInfoAsync(string guestSessionId);
    Task UpdateDishDetailsAsync(string guestSessionId, string dishId, string? note, string? serveTime);
    Task UpdateAllergiesAsync(string guestSessionId, string? allergies);
    Task ReopenCartAsync(string guestSessionId);
    Task<GuestPaymentShare> PayAsync(string guestSessionId);
}

