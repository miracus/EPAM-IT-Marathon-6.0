using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>Who is signed in on the storefront in this scenario's browser.</summary>
public sealed record SignedInOnTheSite(string Persona);

/// <summary>Signs <paramref name="persona"/> in through the storefront's login form. If someone else is signed in
/// already (the organizer before the waiter takes over), their session is cleared first.</summary>
public sealed class SignInToTheSiteAs(string persona) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        if (actor.TryRecall<SignedInOnTheSite>(out var current))
        {
            if (string.Equals(current.Persona, persona, StringComparison.Ordinal))
                return;

            // The storefront keeps its session in local storage; clearing it signs the previous person out.
            await actor.LookingAtThePage().EvaluateAsync("() => localStorage.clear()");
        }

        var account = await Accounts.OfAsync(actor, persona);
        await actor.AttemptsToAsync(new SignInThroughTheLoginForm(account.Email, account.Password));
        actor.Remember(new SignedInOnTheSite(persona));
    }
}
