using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrder;
using Restaurant_Aqa_Framework.Api.Dtos.Orders;
using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>The organizer pre-orders dishes for themselves (the reservation's own pre-order) and opens their own
/// place in the group, as the group page does — after this the organizer is a participant of the group.</summary>
public sealed class PreOrderForThemselves(IReadOnlyList<DishLine> dishes) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var restaurant = actor.Recall<Restaurant>();
        var booking = actor.Recall<GroupBooking>(Personas.Organizer);
        var organizer = await Accounts.OfAsync(actor, Personas.Organizer);
        var response = actor.TheLastResponse();

        var items = dishes.Select(line => new OrderItemRequest(restaurant.Dish(line.Dish).Id, line.Quantity)).ToList();
        await response.CaptureAsync(await new OrdersApi(actor.UsingTheApi())
            .CreateOrderAsync(new CreateOrderRequest(booking.ReservationId, items), organizer.IdToken));
        if (!response.Succeeded)
            throw new InvalidOperationException($"The organizer's own pre-order failed with {response.Status}: {response.Body}");

        await response.CaptureAsync(await new GroupPreOrderApi(actor.UsingTheApi())
            .GetOwnerSessionIdAsync(booking.ReservationId, organizer.IdToken));
        actor.Remember(new OwnSession(response.Json<OwnerSessionIdResponse>().GuestSessionId));
    }
}
