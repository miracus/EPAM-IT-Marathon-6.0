using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>Opens the order of the organizer's reservation as <paramref name="persona"/> (the waiter serving the
/// table, or the organizer). The outcome is the scenario's last response.</summary>
public sealed class ReadTheReservationOrder(string persona) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var booking = actor.Recall<GroupBooking>(Personas.Organizer);
        var token = await Accounts.TokenOfAsync(actor, persona);

        await actor.TheLastResponse().CaptureAsync(
            await new OrdersApi(actor.UsingTheApi()).GetOrderAsync(booking.ReservationId, token));
    }
}
