using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Services.GroupReservations;
using System.Threading.Tasks;

namespace APIHandler.Handlers.GuestReservations;

public class GetGroupReservationHandler(IGroupReservationService groupReservationService) : IApiRequestHandler
{
    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!request.TryGetPathValue("token", out string? token))
            return ResponseBuilder.CreateErrorResponse(400, "Token is requried.", context.Logger);

        try
        {
            // Public route: the owner is recognised by a valid Cognito id token, guests send none.
            string? requesterId = await OptionalCognitoUser.GetUserIdAsync(request);
            var result = await groupReservationService.GetGroupReservationByTokenAsync(token, requesterId);
            return ResponseBuilder.CreateSuccessResponse(result);
        }
        catch(GroupReservationNotFoundException exception)
        {
            return ResponseBuilder.CreateErrorResponse(404, exception.Message, context.Logger, exception);
        }
        catch (ValidationException exception)
        {
            return ResponseBuilder.CreateErrorCodeResponse(exception);
        }
    }
}
