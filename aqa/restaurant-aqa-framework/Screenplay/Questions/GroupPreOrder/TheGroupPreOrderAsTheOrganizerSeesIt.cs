using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrder;
using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Screenplay.Questions;

/// <summary>The group pre-order of <paramref name="organizer"/>'s booking, fetched fresh with that organizer's own
/// account (so it reflects what is stored, not what an earlier step was answered).</summary>
public sealed class TheGroupPreOrderAsTheOrganizerSeesIt(string organizer = Personas.Organizer) : IQuestion<GroupPreOrderView>
{
    public async Task<GroupPreOrderView> AnsweredByAsync(Actor actor)
    {
        var booking = actor.Recall<GroupBooking>(organizer);
        var token = await Accounts.TokenOfAsync(actor, organizer);
        var response = await new GroupPreOrderApi(actor.UsingTheApi()).GetSessionsAsync(booking.GroupReservationId, token);
        if (!response.Ok)
            throw new InvalidOperationException(
                $"{organizer} could not open the group pre-order: {response.Status} {await response.ReadErrorMessageAsync()}");

        var snapshot = await response.ReadAsAsync<GroupSnapshotResponse>();
        var guests = snapshot.Participants
            .Where(p => string.Equals(p.Type, "Guest", StringComparison.Ordinal))
            .Select(p => new ParticipantSummary(
                p.Name,
                DishLines.Describe(p.Items.Select(i => new DishLine(Restaurant.PlainName(i.Dish.Name), i.Quantity))),
                p.Total,
                p.IsSubmitted ? ParticipantSummary.Confirmed : ParticipantSummary.StillChoosing))
            .ToList();

        return new GroupPreOrderView(guests, snapshot.Total, snapshot.Participants.Sum(p => p.Total), snapshot.Participants.Count);
    }
}
