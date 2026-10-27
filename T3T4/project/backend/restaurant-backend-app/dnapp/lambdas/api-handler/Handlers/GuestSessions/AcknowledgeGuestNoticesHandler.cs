using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Models;
using APIHandler.Services.GuestSessions;
using System.Threading.Tasks;

namespace APIHandler.Handlers.GuestSessions;

/// <summary>
/// POST /guest/sessions/{guestSessionId}/notices/ack — US16.
/// The guest confirms they have seen the owner's changes ("OK, got it"). Requires the guest session token.
/// </summary>
public class AcknowledgeGuestNoticesHandler(IGuestSessionService guestSessionService, IGuestAuthorizationService guestAuthorizationService) : IApiRequestHandler
{
    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!request.TryGetPathValue("guestSessionId", out string? guestSessionId))
            return ResponseBuilder.CreateErrorResponse(404, "Guest session not found", context.Logger);

        if (await guestAuthorizationService.ValidateAsync(request, context, guestSessionId) is APIGatewayProxyResponse errorResponse)
            return errorResponse;

        try
        {
            await guestSessionService.AcknowledgeNoticesAsync(guestSessionId);
            return ResponseBuilder.CreateJsonResponse(200, new SuccessResponse("Notices acknowledged."));
        }
        catch (GuestSessionNotFoundException exception)
        {
            return ResponseBuilder.CreateErrorResponse(404, exception.Message, context.Logger);
        }
    }
}
