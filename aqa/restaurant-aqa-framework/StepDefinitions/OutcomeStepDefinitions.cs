using Restaurant_Aqa_Framework.Screenplay;
using Restaurant_Aqa_Framework.Screenplay.Questions;

namespace Restaurant_Aqa_Framework.StepDefinitions;

/// <summary>Business-level outcomes shared by several features ("the visitor is told ...", "could not be
/// found"). How an outcome shows up on the wire — which status code, which error body — is decided here,
/// so feature files never mention it.</summary>
[Binding]
public sealed class OutcomeStepDefinitions(Actor actor) : StepDefinitionBase(actor)
{
    [Then("the visitor is told {string}")]
    [Scope(Tag = "api")]
    public async Task ThenVisitorIsTold(string message)
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());

        await Assert.That(outcome.Succeeded).IsFalse().Because($"the request unexpectedly succeeded: {outcome.Body}");
        await Assert.That(outcome.ErrorMessage).IsEqualTo(message);
    }

    [Then("the organizer is told {string}")]
    [Scope(Tag = "api")]
    public async Task ThenTheOrganizerIsTold(string message)
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());

        await Assert.That(outcome.Succeeded).IsFalse().Because($"the request unexpectedly succeeded: {outcome.Body}");
        await Assert.That(outcome.ErrorMessage).IsEqualTo(message).Because($"response {outcome.Status}: {outcome.Body}");
    }

    /// <summary>Signed in, but not the organizer of this group: forbidden, with the backend's explanation.</summary>
    [Then("they are told that only the organizer can do this")]
    public async Task ThenTheyAreToldOnlyTheOrganizerCan()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());

        await Assert.That(outcome.Status).IsEqualTo(403).Because($"response body was: {outcome.Body}");
        await Assert.That(outcome.ErrorMessage).IsEqualTo("Only the reservation owner can perform this action.");
    }

    /// <summary>No account session (no token, or a guest's session token): the gateway refuses before the service runs.</summary>
    [Then("they are asked to sign in first")]
    public async Task ThenTheyAreAskedToSignIn()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Status).IsEqualTo(401).Because($"response body was: {outcome.Body}");
    }

    [Then("the organizer is told that the guest could not be found")]
    public async Task ThenTheOrganizerIsToldTheGuestIsMissing()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());

        await Assert.That(outcome.Status).IsEqualTo(404).Because($"response body was: {outcome.Body}");
        await Assert.That(outcome.ErrorMessage).Contains("not found");
    }

    /// <summary>Guests confirmed dishes, but the reservation itself has no order the dishes could be added to.</summary>
    [Then("the organizer is told that the reservation has no order to send")]
    public async Task ThenTheOrganizerIsToldThereIsNoOrder()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());

        await Assert.That(outcome.Status).IsEqualTo(400).Because($"response body was: {outcome.Body}");
        await Assert.That(outcome.ErrorMessage).StartsWith("Order for reservation");
        await Assert.That(outcome.ErrorMessage).EndsWith("was not found.");
    }

    [Then("sign-in is refused")]
    public async Task ThenSignInIsRefused()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Status).IsEqualTo(401).Because($"response body was: {outcome.Body}");
    }

    [Then("the guest is told that the {item} could not be found")]
    public async Task ThenGuestIsToldNotFound(string item)
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());

        await Assert.That(outcome.Status).IsEqualTo(404).Because($"response body was: {outcome.Body}");
        await Assert.That(outcome.ErrorMessage).Contains("was not found");
    }

    [Then("the location is not created")]
    public async Task ThenLocationIsNotCreated()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Status).IsGreaterThanOrEqualTo(400).Because($"the location was created: {outcome.Body}");
    }

    [Then("the information is available")]
    [Then("the menu is shown")]
    public async Task ThenInformationIsAvailable()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Succeeded).IsTrue().Because($"response status {outcome.Status}, body: {outcome.Body}");
    }
}
