using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Models;
using APIHandler.Models.GuestSessions;
using APIHandler.Services.GuestSessions;
using System;
using System.Threading.Tasks;

namespace APIHandler.Handlers.GuestSessions;

/// <summary>
/// Guest-side endpoints of the group pre-order extensions. They need the guest session token
/// (Authorization: Bearer {sessionToken}) — no Cognito account.
/// </summary>
internal static class GuestEndpoint
{
    public static async Task<APIGatewayProxyResponse> RunAsync(
        APIGatewayProxyRequest request,
        ILambdaContext context,
        IGuestAuthorizationService authorization,
        string[] pathNames,
        Func<string[], Task<object>> action)
    {
        var values = new string[pathNames.Length];
        for (int i = 0; i < pathNames.Length; i++)
        {
            if (!request.TryGetPathValue(pathNames[i], out var value))
                return ResponseBuilder.CreateErrorResponse(400, $"Missing '{pathNames[i]}' in path parameters.", context.Logger);
            values[i] = value;
        }

        if (await authorization.ValidateAsync(request, context, values[0]) is APIGatewayProxyResponse unauthorized)
            return unauthorized;

        try
        {
            return ResponseBuilder.CreateJsonResponse(200, await action(values));
        }
        catch (GuestSessionNotFoundException exception)
        {
            return ResponseBuilder.CreateErrorResponse(404, exception.Message, context.Logger);
        }
        catch (DishNotFoundException exception)
        {
            return ResponseBuilder.CreateErrorResponse(404, exception.Message, context.Logger);
        }
        catch (GroupReservationSubmittedException exception)
        {
            return ResponseBuilder.CreateErrorResponse(409, exception.Message, context.Logger);
        }
        catch (GroupReservationExpiredException exception)
        {
            return ResponseBuilder.CreateErrorResponse(409, exception.Message, context.Logger);
        }
        catch (ValidationException exception)
        {
            return ResponseBuilder.CreateErrorCodeResponse(exception);
        }
    }
}

/// <summary>PUT /guest/sessions/{guestSessionId}/dishes/{dishId} — US18/US19. Body: { "note": "...", "serveTime": "AfterMains" }.</summary>
public class UpdateGuestDishDetailsHandler(IGuestSessionService service, IGuestAuthorizationService authorization) : IApiRequestHandler
{
    public Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (OwnerEndpoint.ReadBody<DishDetailsRequest>(request) is not DishDetailsRequest body)
            return Task.FromResult(OwnerEndpoint.InvalidBody(context));

        return GuestEndpoint.RunAsync(request, context, authorization, ["guestSessionId", "dishId"], async p =>
        {
            await service.UpdateDishDetailsAsync(p[0], p[1], body.Note, body.ServeTime);
            return new SuccessResponse("Dish details updated.");
        });
    }
}

/// <summary>PUT /guest/sessions/{guestSessionId}/allergies — US18. Body: { "allergies": "Nuts" }.</summary>
public class UpdateGuestAllergiesHandler(IGuestSessionService service, IGuestAuthorizationService authorization) : IApiRequestHandler
{
    public Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (OwnerEndpoint.ReadBody<AllergiesRequest>(request) is not AllergiesRequest body)
            return Task.FromResult(OwnerEndpoint.InvalidBody(context));

        return GuestEndpoint.RunAsync(request, context, authorization, ["guestSessionId"], async p =>
        {
            await service.UpdateAllergiesAsync(p[0], body.Allergies);
            return new SuccessResponse("Allergies updated.");
        });
    }
}

/// <summary>POST /guest/sessions/{guestSessionId}/reopen — US16 AC3 "Pick another dish" after the cart was submitted.</summary>
public class ReopenGuestCartHandler(IGuestSessionService service, IGuestAuthorizationService authorization) : IApiRequestHandler
{
    public Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
        => GuestEndpoint.RunAsync(request, context, authorization, ["guestSessionId"], async p =>
        {
            await service.ReopenCartAsync(p[0]);
            return new SuccessResponse("The cart can be edited again.");
        });
}

/// <summary>POST /guest/sessions/{guestSessionId}/pay — US17, simulated payment of the guest's share.</summary>
public class PayGuestShareHandler(IGuestSessionService service, IGuestAuthorizationService authorization) : IApiRequestHandler
{
    public Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
        => GuestEndpoint.RunAsync(request, context, authorization, ["guestSessionId"],
            async p => await service.PayAsync(p[0]));
}
