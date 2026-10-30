using Microsoft.Playwright;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Api;

/// <summary>The waiter list of a location. Adding a registered user to it gives the account the waiter role the
/// next time it signs in; reservations at the location are then assigned to that waiter.</summary>
public sealed class WaitersApi(IAPIRequestContext api) : ApiClientBase(api)
{
    public Task<IAPIResponse> AddWaiterAsync(string email, string locationId, string? bearerToken = null) =>
        Api.PostAsync(Url("/waiters"), new APIRequestContextOptions
        {
            DataString = Serialize(new WaiterListRequest(email, locationId)),
            Headers = Headers(bearerToken),
        });

    public Task<IAPIResponse> RemoveWaiterAsync(string email, string locationId, string? bearerToken = null) =>
        DeleteAsync(Url("/waiters"), new APIRequestContextOptions
        {
            DataString = Serialize(new WaiterListRequest(email, locationId)),
            Headers = Headers(bearerToken),
        });
}
