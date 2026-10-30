using Restaurant_Aqa_Framework.Api.Dtos.Orders;
using Restaurant_Aqa_Framework.Screenplay;
using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;
using Restaurant_Aqa_Framework.Screenplay.Questions;
using Restaurant_Aqa_Framework.Screenplay.Tasks;
using Restaurant_Aqa_Framework.Support;

namespace Restaurant_Aqa_Framework.StepDefinitions;

/// <summary>What the organizer, the waiter and others do with a group pre-order and what they find. The same
/// sentence is carried out through the API in `@api` scenarios and through the storefront in `@ui` ones.</summary>
[Binding]
public sealed class GroupPreOrderStepDefinitions(Actor actor) : StepDefinitionBase(actor)
{
    // ---- opening the group pre-order

    [Given("the organizer has opened the group pre-order")]
    [When("the organizer opens the group pre-order")]
    [Scope(Tag = "api")]
    public Task WhenTheOrganizerOpensItThroughTheService() => Actor.AttemptsToAsync(new ReviewTheGroupPreOrder());

    [Given("the organizer has opened the group pre-order")]
    [When("the organizer opens the group pre-order")]
    [Scope(Tag = "ui")]
    public Task WhenTheOrganizerOpensThePage() => Actor.AttemptsToAsync(new OpenTheGroupPreOrderPage());

    [When("the organizer refreshes the group pre-order")]
    [Scope(Tag = "ui")]
    public Task WhenTheOrganizerRefreshesThePage() => Actor.AttemptsToAsync(new RefreshTheGroupPreOrderPage());

    [When("the organizer opens a group pre-order that does not exist")]
    [Scope(Tag = "api")]
    public Task WhenTheOrganizerOpensAMissingGroup() => Actor.AttemptsToAsync(ReviewTheGroupPreOrder.OfAGroupThatDoesNotExist());

    [When("the organizer opens a group pre-order that does not exist")]
    [Scope(Tag = "ui")]
    public Task WhenTheOrganizerOpensAMissingGroupPage() => Actor.AttemptsToAsync(OpenTheGroupPreOrderPage.OfAReservationWithoutAGroup());

    [When("{someone} tries to open the group pre-order")]
    public Task WhenSomeoneTriesToOpenIt(string someone) => Actor.AttemptsToAsync(new ReviewTheGroupPreOrder(someone));

    // ---- what the organizer sees

    [Then("the organizer sees these guests")]
    [Scope(Tag = "api")]
    public async Task ThenTheOrganizerSeesTheseGuests(ExpectedGuests expected) =>
        await ExpectGuests(await Actor.AsksForAsync(new TheGroupPreOrderAsTheOrganizerSeesIt()), expected);

    [Then("the organizer sees these guests")]
    [Scope(Tag = "ui")]
    public async Task ThenThePageShowsTheseGuests(ExpectedGuests expected) =>
        await ExpectGuests(await Actor.AsksForAsync(new TheGroupPreOrderPage()), expected);

    [Then("the group total is {int}")]
    [Scope(Tag = "api")]
    public async Task ThenTheGroupTotalIs(int total) =>
        await ExpectTotal(await Actor.AsksForAsync(new TheGroupPreOrderAsTheOrganizerSeesIt()), total);

    [Then("the group total is {int}")]
    [Scope(Tag = "ui")]
    public async Task ThenThePageTotalIs(int total) =>
        await ExpectTotal(await Actor.AsksForAsync(new TheGroupPreOrderPage()), total);

    [Then("the subtotal of {string} is {int}")]
    [Scope(Tag = "api")]
    public async Task ThenTheSubtotalIs(string guest, int subtotal) =>
        await ExpectSubtotal(await Actor.AsksForAsync(new TheGroupPreOrderAsTheOrganizerSeesIt()), guest, subtotal);

    [Then("the subtotal of {string} is {int}")]
    [Scope(Tag = "ui")]
    public async Task ThenThePageSubtotalIs(string guest, int subtotal) =>
        await ExpectSubtotal(await Actor.AsksForAsync(new TheGroupPreOrderPage()), guest, subtotal);

    [Then("the group total equals the sum of everyone's subtotals")]
    [Scope(Tag = "api")]
    public async Task ThenTheTotalIsTheSum() =>
        await ExpectTotalIsTheSum(await Actor.AsksForAsync(new TheGroupPreOrderAsTheOrganizerSeesIt()));

    [Then("the group total equals the sum of everyone's subtotals")]
    [Scope(Tag = "ui")]
    public async Task ThenThePageTotalIsTheSum() =>
        await ExpectTotalIsTheSum(await Actor.AsksForAsync(new TheGroupPreOrderPage()));

    [Then("nobody is listed in the group")]
    public async Task ThenNobodyIsListed()
    {
        var group = await Actor.AsksForAsync(new TheGroupPreOrderAsTheOrganizerSeesIt());
        await Assert.That(group.ParticipantCount).IsEqualTo(0).Because($"the group lists:{Environment.NewLine}{group.DescribeGuests()}");
    }

    [Then("the organizer sees no guests")]
    [Scope(Tag = "ui")]
    public async Task ThenThePageShowsNoGuests()
    {
        var group = await Actor.AsksForAsync(new TheGroupPreOrderPage());
        await Assert.That(group.Guests.Count).IsEqualTo(0).Because($"the page lists:{Environment.NewLine}{group.DescribeGuests()}");
    }

    [Then("{string} is still in the group")]
    public async Task ThenAGuestIsStillInTheGroup(string guest) =>
        await ExpectInTheGroup(await Actor.AsksForAsync(new TheGroupPreOrderAsTheOrganizerSeesIt()), guest);

    [Then("{string} is still in the group of the other organizer")]
    public async Task ThenAGuestIsStillInTheOtherGroup(string guest) =>
        await ExpectInTheGroup(await Actor.AsksForAsync(new TheGroupPreOrderAsTheOrganizerSeesIt(Personas.AnotherOrganizer)), guest);

    [Then("the organizer is told that nobody has chosen any dishes yet")]
    [Scope(Tag = "ui")]
    public async Task ThenThePageSaysNobodyHasChosen()
    {
        var state = await Actor.AsksForAsync(new TheGroupPreOrderPageState());
        await Assert.That(state.ConsolidatedMessage).Contains("haven’t added any dishes yet").Because($"page state: {state}");
    }

    [Then("the organizer is warned that {int} carts are not confirmed yet")]
    [Scope(Tag = "ui")]
    public async Task ThenThePageWarnsAboutUnconfirmedCarts(int carts)
    {
        var state = await Actor.AsksForAsync(new TheGroupPreOrderPageState());
        await Assert.That(state.PendingWarning).StartsWith($"{carts} participants haven’t submitted their cart yet").Because($"page state: {state}");
    }

    [Then("the organizer is told that the group reservation was not found")]
    [Scope(Tag = "ui")]
    public async Task ThenThePageSaysTheGroupWasNotFound()
    {
        var state = await Actor.AsksForAsync(new TheGroupPreOrderPageState());
        await Assert.That(state.NotFound).IsTrue().Because($"page state: {state}");
    }

    // ---- sending the pre-order

    [When("the organizer sends the group pre-order to the restaurant")]
    [Scope(Tag = "api")]
    public Task WhenTheOrganizerSendsIt() => Actor.AttemptsToAsync(new SendTheGroupPreOrder());

    [When("the organizer sends the group pre-order to the restaurant")]
    [Scope(Tag = "ui")]
    public Task WhenTheOrganizerSendsItFromThePage() => Actor.AttemptsToAsync(new SendThePreOrderFromThePage());

    [When("the organizer tries to send the group pre-order to the restaurant")]
    public Task WhenTheOrganizerTriesToSendIt() => Actor.AttemptsToAsync(SendTheGroupPreOrder.Attempt());

    [When("{someone} tries to send the group pre-order to the restaurant")]
    public Task WhenSomeoneTriesToSendIt(string someone) => Actor.AttemptsToAsync(SendTheGroupPreOrder.Attempt(someone));

    [When("the organizer tries to send the group pre-order for a reservation that does not exist")]
    public Task WhenTheOrganizerSendsForAMissingReservation() =>
        Actor.AttemptsToAsync(SendTheGroupPreOrder.ForAReservationThatDoesNotExist());

    [When("the organizer tries to send a group pre-order that does not exist")]
    public Task WhenTheOrganizerSendsAMissingGroup() => Actor.AttemptsToAsync(SendTheGroupPreOrder.ForAGroupThatDoesNotExist());

    [Then("the organizer sees that the pre-order was sent to the restaurant")]
    [Scope(Tag = "ui")]
    public async Task ThenThePageSaysItWasSent()
    {
        var state = await Actor.AsksForAsync(new TheGroupPreOrderPageState());
        await Assert.That(state.Status).IsEqualTo("Sent to Restaurant").Because($"page state: {state}");
    }

    [Then("the pre-order sent to the restaurant is")]
    [Scope(Tag = "api")]
    public async Task ThenTheRestaurantReceived(DishOrder expected) =>
        await ExpectDishes((await Actor.AsksForAsync(new TheOrderOfTheReservation())).Lines, expected, "the restaurant's order");

    [Then("the pre-order sent to the restaurant is")]
    [Scope(Tag = "ui")]
    public async Task ThenThePageShowsWhatWasSent(DishOrder expected) =>
        await ExpectDishes(await Actor.AsksForAsync(new TheConsolidatedPreOrderOnThePage()), expected, "the consolidated pre-order on the page");

    [Then("the restaurant's order is waiting to be confirmed by the kitchen")]
    public async Task ThenTheOrderIsWaitingForTheKitchen()
    {
        var order = await Actor.AsksForAsync(new TheOrderOfTheReservation());
        await Assert.That(order.Status).IsEqualTo(OrderStatus.SubmittedByCustomer).Because($"order: {DishLines.Describe(order.Lines)}");
    }

    [Then("the group pre-order is closed for changes")]
    public async Task ThenTheGroupIsClosed() =>
        await Assert.That(await Actor.AsksForAsync(new TheGroupStatus())).IsEqualTo("Submitted");

    [Then("the group pre-order is still open")]
    public async Task ThenTheGroupIsStillOpen() =>
        await Assert.That(await Actor.AsksForAsync(new TheGroupStatus())).IsEqualTo("Open");

    [Then("the organizer cannot send the pre-order yet")]
    [Scope(Tag = "ui")]
    public async Task ThenThePageDoesNotLetItBeSent()
    {
        var state = await Actor.AsksForAsync(new TheGroupPreOrderPageState());
        await Assert.That(state.CanSend).IsFalse().Because($"page state: {state}");
    }

    [Then("the organizer is told that nothing will be sent to the restaurant")]
    [Scope(Tag = "ui")]
    public async Task ThenThePageSaysNothingWillBeSent()
    {
        var state = await Actor.AsksForAsync(new TheGroupPreOrderPageState());
        await Assert.That(state.ConsolidatedMessage).Contains("nothing will be sent to the restaurant").Because($"page state: {state}");
    }

    // ---- the waiter

    [When("the waiter opens the pre-order of the reservation")]
    [Scope(Tag = "api")]
    public Task WhenTheWaiterReadsTheOrder() => Actor.AttemptsToAsync(new ReadTheReservationOrder(Personas.Waiter));

    [When("the waiter opens the pre-order of the reservation")]
    [Scope(Tag = "ui")]
    public Task WhenTheWaiterOpensThePreOrderPage() => Actor.AttemptsToAsync(new OpenTheWaiterPreOrderPage());

    [Then("the waiter sees these dishes")]
    [Scope(Tag = "api")]
    public async Task ThenTheWaitersOrderHas(DishOrder expected) =>
        await ExpectDishes((await Actor.AsksForAsync(new TheOrderOfTheReservation(Personas.Waiter))).Lines, expected, "the waiter's order");

    [Then("the waiter sees these dishes")]
    [Scope(Tag = "ui")]
    public async Task ThenTheWaiterPageShows(DishOrder expected) =>
        await ExpectDishes(await Actor.AsksForAsync(new TheDishesOnTheWaiterPage()), expected, "the waiter's pre-order page");

    // ---- removing participants

    [When("the organizer removes {string} from the group")]
    [Scope(Tag = "api")]
    public Task WhenTheOrganizerRemovesAGuest(string guest) => Actor.AttemptsToAsync(RemoveFromTheGroup.Guest(guest));

    [When("the organizer removes {string} from the group")]
    [Scope(Tag = "ui")]
    public Task WhenTheOrganizerRemovesAGuestOnThePage(string guest) => Actor.AttemptsToAsync(new RemoveAGuestOnThePage(guest));

    [When("the organizer tries to remove {string} from the group")]
    public Task WhenTheOrganizerTriesToRemove(string guest) => Actor.AttemptsToAsync(RemoveFromTheGroup.AttemptToRemove(guest));

    [When("{someone} tries to remove {string} from the group")]
    public Task WhenSomeoneTriesToRemove(string someone, string guest) =>
        Actor.AttemptsToAsync(RemoveFromTheGroup.AttemptToRemove(guest, someone));

    [When("the organizer tries to remove a guest who does not exist from the group")]
    public Task WhenTheOrganizerRemovesAMissingGuest() => Actor.AttemptsToAsync(RemoveFromTheGroup.AGuestWhoDoesNotExist());

    [When("the organizer tries to remove themselves from the group")]
    public Task WhenTheOrganizerRemovesThemselves() => Actor.AttemptsToAsync(RemoveFromTheGroup.TheOrganizerThemselves());

    // ---- comparisons shared by both stages

    private static async Task ExpectGuests(GroupPreOrderView group, ExpectedGuests expected) =>
        await Assert.That(group.DescribeGuests()).IsEqualTo(expected.Describe())
            .Because($"the organizer sees (total {group.Total}):{Environment.NewLine}{group.DescribeGuests()}");

    private static async Task ExpectTotal(GroupPreOrderView group, int total) =>
        await Assert.That(group.Total).IsEqualTo(total)
            .Because($"guests:{Environment.NewLine}{group.DescribeGuests()}");

    private static async Task ExpectSubtotal(GroupPreOrderView group, string guest, int subtotal)
    {
        var found = group.Guests.FirstOrDefault(g => string.Equals(g.Guest, guest, StringComparison.Ordinal));
        await Assert.That(found).IsNotNull().Because($"{guest} is not listed:{Environment.NewLine}{group.DescribeGuests()}");
        await Assert.That(found!.Subtotal).IsEqualTo(subtotal).Because($"guests:{Environment.NewLine}{group.DescribeGuests()}");
    }

    private static async Task ExpectTotalIsTheSum(GroupPreOrderView group) =>
        await Assert.That(group.Total).IsEqualTo(group.SumOfSubtotals)
            .Because($"guests:{Environment.NewLine}{group.DescribeGuests()}");

    private static async Task ExpectInTheGroup(GroupPreOrderView group, string guest) =>
        await Assert.That(group.Guests.Any(g => string.Equals(g.Guest, guest, StringComparison.Ordinal))).IsTrue()
            .Because($"the group lists:{Environment.NewLine}{group.DescribeGuests()}");

    private static async Task ExpectDishes(IReadOnlyList<DishLine> actual, DishOrder expected, string where) =>
        await Assert.That(DishLines.Describe(actual)).IsEqualTo(expected.Describe())
            .Because($"{where} has: {DishLines.Describe(actual)}");
}
