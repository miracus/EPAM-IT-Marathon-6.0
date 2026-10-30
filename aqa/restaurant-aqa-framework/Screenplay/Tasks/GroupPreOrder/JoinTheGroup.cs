using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrder;
using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>A guest joins a group through its invitation under their own name, puts dishes in their cart and,
/// when <paramref name="confirm"/> is set, confirms it. The guest is remembered under their name.</summary>
public sealed class JoinTheGroup(string guest, IReadOnlyList<DishLine> dishes, bool confirm, string ofOrganizer = Personas.Organizer) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var restaurant = actor.Recall<Restaurant>();
        var booking = actor.Recall<GroupBooking>(ofOrganizer);
        var guests = new GuestSessionsApi(actor.UsingTheApi());
        var response = actor.TheLastResponse();

        await response.CaptureAsync(await guests.JoinAsync(booking.GroupReservationId, guest));
        if (!response.Succeeded)
            throw new InvalidOperationException($"{guest} could not join the group: {response.Status} {response.Body}");
        var session = response.Json<GuestSessionCreatedResponse>();

        foreach (var line in dishes)
        {
            await response.CaptureAsync(await guests.AddDishAsync(
                session.SessionId, session.SessionToken, restaurant.Dish(line.Dish).Id, line.Quantity));
            if (!response.Succeeded)
                throw new InvalidOperationException($"{guest} could not add {line.Dish}: {response.Status} {response.Body}");
        }

        if (confirm)
        {
            await response.CaptureAsync(await guests.SubmitCartAsync(session.SessionId, session.SessionToken));
            if (!response.Succeeded)
                throw new InvalidOperationException($"{guest} could not confirm the cart: {response.Status} {response.Body}");
        }

        actor.Remember(guest, new GuestCart(guest, ofOrganizer, session.SessionId, session.SessionToken, confirm));
    }
}
