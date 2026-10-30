using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>A guest adds a dish to their cart. A confirmed cart is reopened first and confirmed again afterwards,
/// as a guest does through "Edit my order".</summary>
public sealed class AddToTheCart(string guest, string dish, int quantity) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var cart = actor.Recall<GuestCart>(guest);
        var dishId = actor.Recall<Restaurant>().Dish(dish).Id;
        var guests = new GuestSessionsApi(actor.UsingTheApi());
        var response = actor.TheLastResponse();

        if (cart.Confirmed)
            await Require(response, await guests.ReopenCartAsync(cart.SessionId, cart.SessionToken), $"{guest} reopens the cart");

        await Require(response, await guests.AddDishAsync(cart.SessionId, cart.SessionToken, dishId, quantity), $"{guest} adds {dish}");

        if (cart.Confirmed)
            await Require(response, await guests.SubmitCartAsync(cart.SessionId, cart.SessionToken), $"{guest} confirms the cart again");
    }

    private static async Task Require(Contexts.ApiResponseContext response, Microsoft.Playwright.IAPIResponse result, string what)
    {
        await response.CaptureAsync(result);
        if (!response.Succeeded)
            throw new InvalidOperationException($"{what} failed with {response.Status}: {response.Body}");
    }
}
