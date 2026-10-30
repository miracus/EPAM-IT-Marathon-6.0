using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;
using Restaurant_Aqa_Framework.Ui;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>The organizer signs in, finds the booking among their reservations and opens its group pre-order —
/// or, for <see cref="OfAReservationWithoutAGroup"/>, goes straight to the page of a reservation that does not exist.</summary>
public sealed class OpenTheGroupPreOrderPage(bool ofAMissingReservation = false) : ITask
{
    public static OpenTheGroupPreOrderPage OfAReservationWithoutAGroup() => new(ofAMissingReservation: true);

    public async Task PerformAsAsync(Actor actor)
    {
        await actor.AttemptsToAsync(new SignInToTheSiteAs(Personas.Organizer));
        var page = actor.LookingAtThePage();
        var groupPage = new GroupPreOrderPage(page);

        if (ofAMissingReservation)
        {
            await groupPage.NavigateAsync(Guid.NewGuid().ToString());
        }
        else
        {
            var reservations = new ReservationDashboardPage(page);
            await reservations.NavigateAsync();
            await reservations.ReservationByAddress(actor.Recall<Restaurant>().LocationAddress).GroupPreOrderButton.ClickAsync();
        }

        await groupPage.WaitUntilLoadedAsync();
    }
}
