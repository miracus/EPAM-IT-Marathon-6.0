using Microsoft.Playwright;
using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;
using Restaurant_Aqa_Framework.Ui;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>The waiter of the restaurant signs in, finds the organizer's reservation among the reservations
/// assigned to them and opens its pre-order.</summary>
public sealed class OpenTheWaiterPreOrderPage : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        await actor.AttemptsToAsync(new SignInToTheSiteAs(Personas.Waiter));
        var page = actor.LookingAtThePage();

        var reservations = new WaiterReservationsPage(page);
        await reservations.NavigateAsync();
        await reservations.ReservationByAddress(actor.Recall<Restaurant>().LocationAddress).WaiterPreOrderButton.ClickAsync();

        await new WaiterPreOrderPage(page).DishCards.First.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }
}
