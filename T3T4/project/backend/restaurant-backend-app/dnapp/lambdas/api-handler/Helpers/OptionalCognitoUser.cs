using Amazon.Lambda.APIGatewayEvents;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace APIHandler.Helpers;

/// <summary>
/// Reads the caller's Cognito user id on public (authorization type NONE) routes.
/// API Gateway does not validate tokens there, so the id token is validated here.
/// Returns null for anonymous callers, guest-session tokens and invalid or expired tokens.
/// </summary>
public static class OptionalCognitoUser
{
    private const string BearerPrefix = "Bearer ";

    public static async Task<string?> GetUserIdAsync(APIGatewayProxyRequest request)
    {
        string? header = request.Headers?
            .FirstOrDefault(pair => string.Equals(pair.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
            .Value;

        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        string token = header[BearerPrefix.Length..].Trim();

        // A JWT has exactly three dot-separated parts; guest-session tokens are plain Base64.
        if (token.Count(character => character == '.') != 2)
            return null;

        try
        {
            ClaimsIdentity? identity = await CognitoIdTokenValidator.ValidateAsync(token);
            return identity?.FindFirst("sub")?.Value ?? identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
        catch (Exception)
        {
            // A broken token or an unavailable JWKS endpoint only means "not the owner".
            return null;
        }
    }
}
