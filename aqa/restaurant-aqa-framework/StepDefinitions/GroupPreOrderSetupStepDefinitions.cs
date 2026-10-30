using Restaurant_Aqa_Framework.Screenplay;
using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;
using Restaurant_Aqa_Framework.Screenplay.Tasks;
using Restaurant_Aqa_Framework.Support;

namespace Restaurant_Aqa_Framework.StepDefinitions;

/// <summary>Preparing a group pre-order: the restaurant, the organizer's booking, the guests and their carts. The
/// same for `@api` and `@ui` scenarios — guests join through the invitation, so their part is prepared directly.</summary>
[Binding]
public sealed class GroupPreOrderSetupStepDefinitions(Actor actor) : StepDefinitionBase(actor)
{
    [Given("a restaurant with these dishes on its menu")]
    public Task GivenARestaurantWithTheseDishes(Menu menu) =>
        Actor.AttemptsToAsync(new SetUpRestaurantWithMenu(menu.Dishes));

    [Given("the organizer has booked a table for {int} guests")]
    public Task GivenTheOrganizerHasBooked(int guests) =>
        Actor.AttemptsToAsync(new BookAGroupTable(Personas.Organizer, guests, daysAhead: 3));

    [Given("another organizer has booked a table for {int} guests")]
    public Task GivenAnotherOrganizerHasBooked(int guests) =>
        Actor.AttemptsToAsync(new BookAGroupTable(Personas.AnotherOrganizer, guests, daysAhead: 4));

    [Given("the organizer has pre-ordered for themselves")]
    public Task GivenTheOrganizerHasPreOrdered(DishOrder dishes) =>
        Actor.AttemptsToAsync(new PreOrderForThemselves(dishes.Lines));

    [Given("{string} has joined the group and confirmed a cart with")]
    public Task GivenAGuestHasConfirmed(string guest, DishOrder dishes) =>
        Actor.AttemptsToAsync(new JoinTheGroup(guest, dishes.Lines, confirm: true));

    [Given("{string} has joined the group and is still choosing, with")]
    public Task GivenAGuestIsStillChoosing(string guest, DishOrder dishes) =>
        Actor.AttemptsToAsync(new JoinTheGroup(guest, dishes.Lines, confirm: false));

    [Given("{string} has joined the group without choosing anything")]
    public Task GivenAGuestWithoutDishes(string guest) =>
        Actor.AttemptsToAsync(new JoinTheGroup(guest, [], confirm: false));

    [Given("{string} has joined the group of the other organizer and confirmed a cart with")]
    public Task GivenAGuestOfTheOtherGroup(string guest, DishOrder dishes) =>
        Actor.AttemptsToAsync(new JoinTheGroup(guest, dishes.Lines, confirm: true, Personas.AnotherOrganizer));

    [When("{string} adds {int} x {string} to their cart")]
    public Task WhenAGuestAddsADish(string guest, int quantity, string dish) =>
        Actor.AttemptsToAsync(new AddToTheCart(guest, dish, quantity));

    [Given("the organizer has taken {string} out of the cart of {string}")]
    public Task GivenTheOrganizerHasTakenADishOut(string dish, string guest) =>
        Actor.AttemptsToAsync(new TakeADishOutOfAGuestCart(guest, dish));

    [Given("the organizer has sent the group pre-order to the restaurant")]
    public Task GivenTheOrganizerHasSent() => Actor.AttemptsToAsync(new SendTheGroupPreOrder());
}
