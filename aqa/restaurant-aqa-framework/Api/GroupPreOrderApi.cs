using Microsoft.Playwright;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Api;

/// <summary>The organizer's side of a group pre-order. Every call takes the caller's Cognito id token, so the same
/// client is used to check that someone who is not the organizer (or nobody at all) is refused.</summary>
public sealed class GroupPreOrderApi(IAPIRequestContext api) : ApiClientBase(api)
{
    public Task<IAPIResponse> GetInviteTokenAsync(string reservationId, string? bearerToken) =>
        GetAsync(Url($"/reservations/{reservationId}/invite-token"), Authorized(bearerToken));

    /// <summary>Opening the invitation is what creates the group, so it is also how a test learns the group id.</summary>
    public Task<IAPIResponse> OpenInviteAsync(string inviteToken, string? bearerToken = null) =>
        GetAsync(Url($"/guest/invites/{Uri.EscapeDataString(inviteToken)}"), Authorized(bearerToken));

    /// <summary>Returns (and on the first call creates) the organizer's own session, as the group page does.</summary>
    public Task<IAPIResponse> GetOwnerSessionIdAsync(string reservationId, string? bearerToken) =>
        GetAsync(Url($"/reservations/{reservationId}/guest-session-id"), Authorized(bearerToken));

    public Task<IAPIResponse> GetSessionsAsync(string groupReservationId, string? bearerToken) =>
        GetAsync(Url($"/guest/groups/{groupReservationId}/sessions"), Authorized(bearerToken));

    public Task<IAPIResponse> GetStatusAsync(string groupReservationId) =>
        GetAsync(Url($"/guest/groups/{groupReservationId}/status"));

    public Task<IAPIResponse> SubmitAsync(string groupReservationId, string reservationId, string? bearerToken) =>
        Api.PostAsync(Url($"/group-reservations/{groupReservationId}/submit"), new APIRequestContextOptions
        {
            DataString = Serialize(new SubmitGroupPreOrderRequest(reservationId)),
            Headers = Headers(bearerToken),
        });

    public Task<IAPIResponse> RemoveParticipantAsync(string groupReservationId, string guestSessionId, string? bearerToken) =>
        DeleteAsync(Url($"/guest/groups/{groupReservationId}/sessions/{guestSessionId}"), Authorized(bearerToken));

    public Task<IAPIResponse> RemoveDishFromParticipantAsync(
        string groupReservationId, string guestSessionId, string dishId, string? bearerToken) =>
        DeleteAsync(Url($"/guest/groups/{groupReservationId}/sessions/{guestSessionId}/dishes/{dishId}"), Authorized(bearerToken));

    private static APIRequestContextOptions Authorized(string? bearerToken) => new() { Headers = Headers(bearerToken) };
}
