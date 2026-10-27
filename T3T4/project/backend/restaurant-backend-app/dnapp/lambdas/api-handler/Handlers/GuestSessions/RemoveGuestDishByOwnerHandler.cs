using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Helpers;
using APIHandler.Services.GuestSessions;
using System;
using System.Threading.Tasks;

namespace APIHandler.Handlers.GuestSessions;

/// <summary>
/// DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId}/dishes/{dishId} — US15.
/// Owner removes one dish from a guest's order; the guest gets a notice (US16).
/// Returns the updated group snapshot.
/// </summary>
public class RemoveGuestDishByOwnerHandler(IGroupPreOrderService groupPreOrderService) : IApiRequestHandler
{
    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!request.TryGetPathValue("groupReservationId", out var groupReservationId))
            return GroupPreOrderResponses.MissingPath("groupReservationId", context);

        if (!request.TryGetPathValue("guestSessionId", out var guestSessionId))
            return GroupPreOrderResponses.MissingPath("guestSessionId", context);

        if (!request.TryGetPathValue("dishId", out var dishId))
            return GroupPreOrderResponses.MissingPath("dishId", context);

        var userId = UserContextHelper.GetAuthenticatedUserId(request);
        if (userId is null)
            return GroupPreOrderResponses.Unauthorized(context);

        try
        {
            var snapshot = await groupPreOrderService.RemoveGuestDishAsync(userId, groupReservationId, guestSessionId, dishId);
            return ResponseBuilder.CreateJsonResponse(200, snapshot);
        }
        catch (Exception exception) when (GroupPreOrderResponses.TryMap(exception, context, out var response))
        {
            return response;
        }
    }
}
