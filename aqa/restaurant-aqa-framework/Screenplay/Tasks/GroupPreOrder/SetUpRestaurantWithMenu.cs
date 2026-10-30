using System.Text.Json;
using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.Dishes;
using Restaurant_Aqa_Framework.Api.Dtos.Locations;
using Restaurant_Aqa_Framework.Api.Dtos.Tables;
using Restaurant_Aqa_Framework.Api.Seeding;
using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>Sets up a restaurant of the scenario's own: a location with the given dishes on its menu, a table large
/// enough for a group, and a waiter (a freshly registered account put on the location's waiter list), so every
/// reservation at it is served by a waiter the scenario can sign in as. Everything is removed afterwards.</summary>
public sealed class SetUpRestaurantWithMenu(IReadOnlyList<(string Dish, decimal Price)> menu, int tableSeats = 8) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var cleanup = actor.Recall<CleanupScope>();
        var waiter = await Accounts.OfAsync(actor, Personas.Waiter);
        var token = waiter.IdToken;

        var locations = new LocationsApi(actor.UsingTheApi());
        var location = await CreateAsync<LocationResponse>(actor, "the location",
            () => locations.CreateLocationAsync(SampleDataFactory.CreateLocationRequest(), token));
        cleanup.TrackDelete($"location:{location.Id}", () => locations.DeleteLocationAsync(location.Id, force: true, bearerToken: token));

        // The waiter goes on the location's list first and signs in again, so the token carries the waiter role:
        // removing a table needs it. Cleanup runs in reverse, so the waiter leaves the list only after the
        // dishes and the table are gone.
        var waiters = new WaitersApi(actor.UsingTheApi());
        var added = await waiters.AddWaiterAsync(waiter.Email, location.Id, token);
        await actor.TheLastResponse().CaptureAsync(added);
        if (!added.Ok)
            throw new InvalidOperationException($"Putting the waiter on the location's list failed with {added.Status}: {actor.TheLastResponse().Body}");
        cleanup.TrackDelete($"waiter:{waiter.Email}", () => waiters.RemoveWaiterAsync(waiter.Email, location.Id, token));
        token = (await Accounts.RefreshAsync(actor, Personas.Waiter)).IdToken;

        var dishesApi = new DishesApi(actor.UsingTheApi());
        var dishes = new Dictionary<string, DishResponse>(StringComparer.Ordinal);
        foreach (var (name, price) in menu)
        {
            var request = SampleDataFactory.CreateDishRequest([location.Id]) with { Name = SampleDataFactory.Prefix + name, Price = price };
            var dish = await CreateAsync<DishResponse>(actor, $"the dish '{name}'", () => dishesApi.CreateDishAsync(request, token));
            cleanup.TrackDelete($"dish:{dish.Id}", () => dishesApi.DeleteDishAsync(dish.Id, force: true, bearerToken: token));
            dishes[name] = dish;
        }

        var tables = new TablesApi(actor.UsingTheApi());
        var table = await CreateAsync<TableResponse>(actor, "the table",
            () => tables.CreateTableAsync(SampleDataFactory.CreateTableRequest(location.Id, tableSeats), token));
        cleanup.TrackDelete($"table:{table.Id}", () => tables.DeleteTableAsync(table.Id, token));

        actor.Remember(new Restaurant(location.Id, location.Address, table.Id, dishes));
    }

    private static async Task<T> CreateAsync<T>(Actor actor, string what, Func<Task<Microsoft.Playwright.IAPIResponse>> create)
    {
        var response = actor.TheLastResponse();
        await response.CaptureAsync(await create());
        if (!response.Succeeded)
            throw new InvalidOperationException($"Setting up {what} failed with {response.Status}: {response.Body}");

        return JsonSerializer.Deserialize<T>(response.Body, JsonDefaults.Options)
            ?? throw new InvalidOperationException($"Setting up {what} returned an empty body.");
    }
}
