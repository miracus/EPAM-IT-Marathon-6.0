using System.Globalization;
using System.Text.Json;
using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrder;
using Restaurant_Aqa_Framework.Api.Dtos.Reservations;
using Restaurant_Aqa_Framework.Api.Seeding;
using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>A customer books the scenario's table for a group and opens the invitation, which turns the
/// reservation into a group pre-order. The booking is days ahead, so the group stays open for changes (changes
/// close two hours before the visit); each organizer gets a different day, so their bookings never overlap.</summary>
public sealed class BookAGroupTable(string persona, int guests, int daysAhead) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var restaurant = actor.Recall<Restaurant>();
        var organizer = await Accounts.OfAsync(actor, persona);
        var response = actor.TheLastResponse();

        var start = DateTime.UtcNow.Date.AddDays(daysAhead).AddHours(12);
        var reservations = new ReservationsApi(actor.UsingTheApi());
        await response.CaptureAsync(await reservations.CreateReservationAsync(
            new CreateReservationRequest(restaurant.TableId, start, start.AddMinutes(90), guests), organizer.IdToken));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Booking the table for {guests} failed with {response.Status}: {response.Body}");

        var reservationId = response.JsonElement().GetProperty("id").GetString()
            ?? throw new InvalidOperationException($"The booking has no id: {response.Body}");
        actor.Recall<CleanupScope>().TrackDelete(
            $"reservation:{reservationId}", () => reservations.CancelReservationAsync(reservationId, organizer.IdToken));

        var groups = new GroupPreOrderApi(actor.UsingTheApi());
        await response.CaptureAsync(await groups.GetInviteTokenAsync(reservationId, organizer.IdToken));
        var inviteToken = response.Json<InviteTokenResponse>().Token;

        await response.CaptureAsync(await groups.OpenInviteAsync(inviteToken, organizer.IdToken));
        var invite = response.Json<GroupInviteResponse>();

        actor.Remember(persona, new GroupBooking(reservationId, invite.GroupReservationId, inviteToken));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[GroupPreOrder] {persona}: reservation {reservationId}, group {invite.GroupReservationId}, {start:yyyy-MM-dd HH:mm} UTC"));
    }
}
