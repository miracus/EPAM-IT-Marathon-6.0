using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Helpers;
using APIHandler.Models.GuestSessions;
using APIHandler.Services.GuestSessions;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace APIHandler.Handlers.GuestSessions;

/// <summary>
/// Owner-side endpoints of the group pre-order extensions (US13–US21).
/// All of them require the Cognito id token of the reservation owner and use the same error mapping
/// as the homework endpoints: 403 not the owner · 404 not found · 409 submitted/closed · 400 validation.
/// </summary>
internal static class OwnerEndpoint
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<APIGatewayProxyResponse> RunAsync(
        APIGatewayProxyRequest request,
        ILambdaContext context,
        string[] pathNames,
        Func<string, string[], Task<object>> action)
    {
        var values = new string[pathNames.Length];
        for (int i = 0; i < pathNames.Length; i++)
        {
            if (!request.TryGetPathValue(pathNames[i], out var value))
                return GroupPreOrderResponses.MissingPath(pathNames[i], context);
            values[i] = value;
        }

        var userId = UserContextHelper.GetAuthenticatedUserId(request);
        if (userId is null)
            return GroupPreOrderResponses.Unauthorized(context);

        try
        {
            object result = await action(userId, values);
            return ResponseBuilder.CreateJsonResponse(200, result);
        }
        catch (Exception exception) when (GroupPreOrderResponses.TryMap(exception, context, out var response))
        {
            return response;
        }
    }

    /// <summary>Optional JSON body: an empty body gives a new instance, invalid JSON gives null.</summary>
    public static T? ReadBody<T>(APIGatewayProxyRequest request) where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(request.Body))
            return new T();

        try
        {
            return JsonSerializer.Deserialize<T>(request.Body, s_jsonOptions) ?? new T();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static APIGatewayProxyResponse InvalidBody(ILambdaContext context)
        => ResponseBuilder.CreateErrorResponse(400, "Invalid json body", context.Logger);
}

/// <summary>POST /guest/groups/{groupReservationId}/sessions/{guestSessionId}/dishes/{dishId}/restore — US15 AC2 Undo.</summary>
public class RestoreGuestDishHandler(IGroupPreOrderService service) : IApiRequestHandler
{
    public Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (OwnerEndpoint.ReadBody<RestoreDishRequest>(request) is not RestoreDishRequest body)
            return Task.FromResult(OwnerEndpoint.InvalidBody(context));

        return OwnerEndpoint.RunAsync(request, context, ["groupReservationId", "guestSessionId", "dishId"],
            async (userId, p) => await service.RestoreGuestDishAsync(userId, p[0], p[1], p[2], body));
    }
}

/// <summary>POST /guest/groups/{groupReservationId}/sessions/{guestSessionId}/change-request — US15 AC4.</summary>
public class RequestGuestChangeHandler(IGroupPreOrderService service) : IApiRequestHandler
{
    public Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
        => OwnerEndpoint.RunAsync(request, context, ["groupReservationId", "guestSessionId"],
            async (userId, p) => await service.RequestChangeAsync(userId, p[0], p[1]));
}

/// <summary>POST /guest/groups/{groupReservationId}/reminders — US14. Body (optional): { "guestSessionIds": [...] }.</summary>
public class SendGroupRemindersHandler(IGroupPreOrderService service) : IApiRequestHandler
{
    public Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (OwnerEndpoint.ReadBody<RemindRequest>(request) is not RemindRequest body)
            return Task.FromResult(OwnerEndpoint.InvalidBody(context));

        return OwnerEndpoint.RunAsync(request, context, ["groupReservationId"],
            async (userId, p) => await service.SendRemindersAsync(userId, p[0], body));
    }
}

/// <summary>PUT /guest/groups/{groupReservationId}/settings — US14 AC3. Body: { "autoRemind": true }.</summary>
public class UpdateGroupSettingsHandler(IGroupPreOrderService service) : IApiRequestHandler
{
    public Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (OwnerEndpoint.ReadBody<GroupSettingsRequest>(request) is not GroupSettingsRequest body)
            return Task.FromResult(OwnerEndpoint.InvalidBody(context));

        return OwnerEndpoint.RunAsync(request, context, ["groupReservationId"],
            async (userId, p) => await service.UpdateSettingsAsync(userId, p[0], body));
    }
}

/// <summary>PUT /guest/groups/{groupReservationId}/my-dishes/{dishId} — US18/US19 for the owner's own dishes.</summary>
public class UpdateOwnerDishHandler(IGroupPreOrderService service) : IApiRequestHandler
{
    public Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (OwnerEndpoint.ReadBody<DishDetailsRequest>(request) is not DishDetailsRequest body)
            return Task.FromResult(OwnerEndpoint.InvalidBody(context));

        return OwnerEndpoint.RunAsync(request, context, ["groupReservationId", "dishId"],
            async (userId, p) => await service.UpdateOwnerDishAsync(userId, p[0], p[1], body));
    }
}

/// <summary>PUT /guest/groups/{groupReservationId}/my-allergies — US18 for the owner.</summary>
public class UpdateOwnerAllergiesHandler(IGroupPreOrderService service) : IApiRequestHandler
{
    public Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (OwnerEndpoint.ReadBody<AllergiesRequest>(request) is not AllergiesRequest body)
            return Task.FromResult(OwnerEndpoint.InvalidBody(context));

        return OwnerEndpoint.RunAsync(request, context, ["groupReservationId"],
            async (userId, p) => await service.UpdateOwnerAllergiesAsync(userId, p[0], body));
    }
}

/// <summary>GET /guest/groups/{groupReservationId}/payment — US17.</summary>
public class GetGroupPaymentHandler(IGroupPreOrderService service) : IApiRequestHandler
{
    public Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
        => OwnerEndpoint.RunAsync(request, context, ["groupReservationId"],
            async (userId, p) => await service.GetPaymentAsync(userId, p[0]));
}

/// <summary>PUT /guest/groups/{groupReservationId}/payment — US17. Body: { "mode": "EachPaysOwn" | "OwnerPaysAll" | "SplitEqually" }.</summary>
public class SetPaymentModeHandler(IGroupPreOrderService service) : IApiRequestHandler
{
    public Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (OwnerEndpoint.ReadBody<PaymentModeRequest>(request) is not PaymentModeRequest body)
            return Task.FromResult(OwnerEndpoint.InvalidBody(context));

        return OwnerEndpoint.RunAsync(request, context, ["groupReservationId"],
            async (userId, p) => await service.SetPaymentModeAsync(userId, p[0], body.Mode));
    }
}

/// <summary>POST /guest/groups/{groupReservationId}/payment/links — US17 AC4 (in-app, simulated).</summary>
public class SendPaymentLinksHandler(IGroupPreOrderService service) : IApiRequestHandler
{
    public Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
        => OwnerEndpoint.RunAsync(request, context, ["groupReservationId"],
            async (userId, p) => await service.SendPaymentLinksAsync(userId, p[0]));
}

/// <summary>POST /guest/groups/{groupReservationId}/payment/{guestSessionId}/paid — US17: owner marks a share as paid.</summary>
public class MarkParticipantPaidHandler(IGroupPreOrderService service) : IApiRequestHandler
{
    public Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
        => OwnerEndpoint.RunAsync(request, context, ["groupReservationId", "guestSessionId"],
            async (userId, p) => await service.MarkPaidAsync(userId, p[0], p[1]));
}

/// <summary>POST /reservations/{reservationId}/rebook — US21. Body: { "previousReservationId": "..." }.</summary>
public class RebookReservationHandler(IGroupPreOrderService service) : IApiRequestHandler
{
    public Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (OwnerEndpoint.ReadBody<RebookRequest>(request) is not RebookRequest body
            || string.IsNullOrWhiteSpace(body.PreviousReservationId))
        {
            return Task.FromResult(ResponseBuilder.CreateErrorResponse(400, "previousReservationId is required.", context.Logger));
        }

        return OwnerEndpoint.RunAsync(request, context, ["reservationId"],
            async (userId, p) => await service.RebookAsync(userId, p[0], body.PreviousReservationId));
    }
}

public class RebookRequest
{
    public string PreviousReservationId { get; set; } = string.Empty;
}
