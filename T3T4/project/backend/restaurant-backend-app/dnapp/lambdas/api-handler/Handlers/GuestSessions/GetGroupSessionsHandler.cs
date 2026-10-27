using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Helpers;
using APIHandler.Services.GuestSessions;
using System;
using System.Threading.Tasks;

namespace APIHandler.Handlers.GuestSessions;

/// <summary>
/// GET /guest/groups/{groupReservationId}/sessions — homework L1.
/// Owner sees every participant, their dishes, per-participant totals and the group total.
/// </summary>
public class GetGroupSessionsHandler(IGroupPreOrderService groupPreOrderService) : IApiRequestHandler
{
    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!request.TryGetPathValue("groupReservationId", out var groupReservationId))
            return GroupPreOrderResponses.MissingPath("groupReservationId", context);

        var userId = UserContextHelper.GetAuthenticatedUserId(request);
        if (userId is null)
            return GroupPreOrderResponses.Unauthorized(context);

        try
        {
            var snapshot = await groupPreOrderService.GetSessionsAsync(userId, groupReservationId);
            return ResponseBuilder.CreateJsonResponse(200, snapshot);
        }
        catch (Exception exception) when (GroupPreOrderResponses.TryMap(exception, context, out var response))
        {
            return response;
        }
    }
}
