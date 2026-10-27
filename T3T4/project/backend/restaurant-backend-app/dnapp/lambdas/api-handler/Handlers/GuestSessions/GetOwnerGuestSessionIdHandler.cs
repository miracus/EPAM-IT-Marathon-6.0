using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Helpers;
using APIHandler.Services.GuestSessions;
using System;
using System.Threading.Tasks;

namespace APIHandler.Handlers.GuestSessions;

/// <summary>
/// GET /reservations/{reservationId}/guest-session-id — homework L2.
/// Returns the owner's own guest session in the group (created on first request).
/// </summary>
public class GetOwnerGuestSessionIdHandler(IGroupPreOrderService groupPreOrderService) : IApiRequestHandler
{
    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!request.TryGetPathValue("reservationId", out var reservationId))
            return GroupPreOrderResponses.MissingPath("reservationId", context);

        var userId = UserContextHelper.GetAuthenticatedUserId(request);
        if (userId is null)
            return GroupPreOrderResponses.Unauthorized(context);

        try
        {
            var result = await groupPreOrderService.GetOwnerSessionIdAsync(userId, reservationId);
            return ResponseBuilder.CreateJsonResponse(200, result);
        }
        catch (Exception exception) when (GroupPreOrderResponses.TryMap(exception, context, out var response))
        {
            return response;
        }
    }
}
