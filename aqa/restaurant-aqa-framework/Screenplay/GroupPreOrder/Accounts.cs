using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.Auth;
using Restaurant_Aqa_Framework.Api.Seeding;

namespace Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;

/// <summary>Accounts the group pre-order scenarios act as. Every scenario registers its own people (fresh
/// "AQA-Sample-" customers), so scenarios never share an account and can run in parallel.</summary>
public static class Accounts
{
    /// <summary>The account remembered for <paramref name="persona"/>, registering and signing it in the first time.</summary>
    public static async Task<Account> OfAsync(Actor actor, string persona)
    {
        if (actor.TryRecall<Account>(persona, out var account))
            return account;

        var visitor = SampleDataFactory.RegisterRequest();
        var auth = new AuthApi(actor.UsingTheApi());
        var registered = await auth.RegisterAsync(visitor);
        if (!registered.Ok)
            throw new InvalidOperationException(
                $"Registering {persona} failed with {registered.Status}: {await registered.ReadErrorMessageAsync()}");

        account = new Account(visitor.Email, visitor.Password, visitor.FirstName, await SignInAsync(actor, visitor.Email, visitor.Password));
        actor.Remember(persona, account);
        return account;
    }

    /// <summary>Signs the account in again, e.g. after it was put on a waiter list, so the new token carries the new role.</summary>
    public static async Task<Account> RefreshAsync(Actor actor, string persona)
    {
        var account = actor.Recall<Account>(persona);
        account = account with { IdToken = await SignInAsync(actor, account.Email, account.Password) };
        actor.Remember(persona, account);
        return account;
    }

    /// <summary>What <paramref name="persona"/> presents to the API: an id token, a guest's session token, or nothing.</summary>
    public static async Task<string?> TokenOfAsync(Actor actor, string persona) => persona switch
    {
        Personas.AnonymousVisitor => null,
        Personas.GuestOfTheGroup => actor.RecallAll<GuestCart>() is { Count: > 0 } guests
            ? guests[0].SessionToken
            : throw new InvalidOperationException("No guest has joined the group yet, so there is no guest to act as."),
        _ => (await OfAsync(actor, persona)).IdToken,
    };

    private static async Task<string> SignInAsync(Actor actor, string email, string password)
    {
        var response = await new AuthApi(actor.UsingTheApi()).SignInAsync(new SignInRequest(email, password));
        if (!response.Ok)
            throw new InvalidOperationException($"Signing in as {email} failed with {response.Status}: {await response.ReadErrorMessageAsync()}");

        return (await response.ReadAsAsync<SignInResponse>()).IdToken;
    }
}
