using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Helpers;
using APIHandler.Models.GuestSessions;
using APIHandler.Services.GuestSessions;
using System;
using System.Threading.Tasks;

namespace APIHandler.Handlers.GuestSessions;

/// <summary>
/// POST /group-reservations/{groupReservationId}/submit — homework L2.
/// Body: { "reservationId": "..." }. Merges submitted guest sessions into the order,
/// sets the order to SubmittedByCustomer and the group to Submitted.
/// </summary>
public class SubmitGroupPreOrderHandler(IGroupPreOrderService groupPreOrderService) : IApiRequestHandler
{
    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!request.TryGetPathValue("groupReservationId", out var groupReservationId))
            return GroupPreOrderResponses.MissingPath("groupReservationId", context);

        var userId = UserContextHelper.GetAuthenticatedUserId(request);
        if (userId is null)
            return GroupPreOrderResponses.Unauthorized(context);

        if (!RequestBodyHelper.TryParseAndValidate(request, context, out SubmitGroupPreOrderRequest? body, out var errorResponse))
            return errorResponse;

        try
        {
            var merged = await groupPreOrderService.SubmitAsync(userId, groupReservationId, body.ReservationId);
            return ResponseBuilder.CreateJsonResponse(200, merged);
        }
        catch (Exception exception) when (GroupPreOrderResponses.TryMap(exception, context, out var response))
        {
            return response;
        }
    }
}
