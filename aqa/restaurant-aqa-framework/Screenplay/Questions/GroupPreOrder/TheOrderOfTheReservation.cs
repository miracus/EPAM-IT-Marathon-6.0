using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.Orders;
using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Screenplay.Questions;

/// <summary>The order of the organizer's reservation, read with <paramref name="persona"/>'s account — the organizer,
/// or the waiter serving the table (which shows what the waiter is given).</summary>
public sealed class TheOrderOfTheReservation(string persona = Personas.Organizer) : IQuestion<ReservationOrder>
{
    public async Task<ReservationOrder> AnsweredByAsync(Actor actor)
    {
        var booking = actor.Recall<GroupBooking>(Personas.Organizer);
        var token = await Accounts.TokenOfAsync(actor, persona);
        var response = await new OrdersApi(actor.UsingTheApi()).GetOrderAsync(booking.ReservationId, token);
        if (!response.Ok)
            throw new InvalidOperationException(
                $"{persona} could not open the order of the reservation: {response.Status} {await response.ReadErrorMessageAsync()}");

        var order = await response.ReadAsAsync<GetOrderResponse>();
        var lines = DishLines.Merge(order.OrderItems
            .Where(item => item.Quantity > 0)
            .Select(item => new DishLine(Restaurant.PlainName(item.Dish.Name), item.Quantity)));
        return new ReservationOrder(lines, order.Status);
    }
}
