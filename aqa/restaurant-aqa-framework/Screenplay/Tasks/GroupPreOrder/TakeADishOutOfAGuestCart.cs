using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>The organizer takes a dish out of a guest's cart (the cart stays confirmed).</summary>
public sealed class TakeADishOutOfAGuestCart(string guest, string dish) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var cart = actor.Recall<GuestCart>(guest);
        var booking = actor.Recall<GroupBooking>(cart.Persona);
        var organizer = await Accounts.OfAsync(actor, cart.Persona);
        var response = actor.TheLastResponse();

        await response.CaptureAsync(await new GroupPreOrderApi(actor.UsingTheApi()).RemoveDishFromParticipantAsync(
            booking.GroupReservationId, cart.SessionId, actor.Recall<Restaurant>().Dish(dish).Id, organizer.IdToken));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Taking {dish} out of {guest}'s cart failed with {response.Status}: {response.Body}");
    }
}
