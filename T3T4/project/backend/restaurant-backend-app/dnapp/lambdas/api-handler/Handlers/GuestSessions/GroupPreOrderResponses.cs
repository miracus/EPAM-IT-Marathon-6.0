using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using System;

namespace APIHandler.Handlers.GuestSessions;

/// <summary>
/// Shared HTTP mapping for the owner-side group pre-order endpoints:
/// 403 not the owner · 404 reservation/group/session/dish not found ·
/// 409 group already submitted or closed · 400 any other validation error.
/// </summary>
internal static class GroupPreOrderResponses
{
    public static bool TryMap(Exception exception, ILambdaContext context, out APIGatewayProxyResponse response)
    {
        switch (exception)
        {
            case ActionForbiddenException:
                response = ResponseBuilder.CreateErrorResponse(403, exception.Message, context.Logger);
                return true;

            case GroupReservationNotFoundException:
            case ReservationNotFoundException:
            case GuestSessionNotFoundException:
            case DishNotFoundException:
                response = ResponseBuilder.CreateErrorResponse(404, exception.Message, context.Logger);
                return true;

            case GroupReservationSubmittedException:
            case GroupReservationExpiredException:
                response = ResponseBuilder.CreateErrorResponse(409, exception.Message, context.Logger);
                return true;

            case ValidationException validationException:
                response = ResponseBuilder.CreateErrorCodeResponse(validationException);
                return true;

            default:
                response = null!;
                return false;
        }
    }

    public static APIGatewayProxyResponse Unauthorized(ILambdaContext context)
        => ResponseBuilder.CreateErrorResponse(401, "Unauthorized.", context.Logger);

    public static APIGatewayProxyResponse MissingPath(string name, ILambdaContext context)
        => ResponseBuilder.CreateErrorResponse(400, $"Missing '{name}' in path parameters.", context.Logger);
}
