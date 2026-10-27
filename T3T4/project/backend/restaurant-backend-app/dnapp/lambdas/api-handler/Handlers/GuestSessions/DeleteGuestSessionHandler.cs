using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Helpers;
using APIHandler.Services.GuestSessions;
using System;
using System.Threading.Tasks;

namespace APIHandler.Handlers.GuestSessions;

/// <summary>
/// DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId} — homework L3.
/// Owner removes a participant and gets the updated group snapshot.
/// </summary>
public class DeleteGuestSessionHandler(IGroupPreOrderService groupPreOrderService) : IApiRequestHandler
{
    private readonly IGroupPreOrderService _service = groupPreOrderService ?? throw new ArgumentNullException(nameof(groupPreOrderService));

    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!request.TryGetPathValue("groupReservationId", out var groupReservationId))
            return GroupPreOrderResponses.MissingPath("groupReservationId", context);

        if (!request.TryGetPathValue("guestSessionId", out var guestSessionId))
            return GroupPreOrderResponses.MissingPath("guestSessionId", context);

        var userId = UserContextHelper.GetAuthenticatedUserId(request);
        if (userId is null)
            return GroupPreOrderResponses.Unauthorized(context);

        try
        {
            var snapshot = await _service.DeleteSessionAsync(userId, groupReservationId, guestSessionId);
            return ResponseBuilder.CreateJsonResponse(200, snapshot);
        }
        catch (Exception exception) when (GroupPreOrderResponses.TryMap(exception, context, out var response))
        {
            return response;
        }
    }
}