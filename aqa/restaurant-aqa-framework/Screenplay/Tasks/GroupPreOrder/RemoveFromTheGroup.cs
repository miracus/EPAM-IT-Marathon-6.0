using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Screenplay.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>Removes a participant from the organizer's group as <paramref name="persona"/>. The participant is a
/// guest remembered by name (possibly a guest of another group), or a session given directly.</summary>
public sealed class RemoveFromTheGroup : ITask
{
    private readonly string? _guest;
    private readonly Func<Actor, string>? _sessionId;
    private readonly string _persona;
    private readonly bool _mustSucceed;

    private RemoveFromTheGroup(string? guest, Func<Actor, string>? sessionId, string persona, bool mustSucceed)
    {
        _guest = guest;
        _sessionId = sessionId;
        _persona = persona;
        _mustSucceed = mustSucceed;
    }

    public static RemoveFromTheGroup Guest(string guest) => new(guest, null, Personas.Organizer, mustSucceed: true);

    public static RemoveFromTheGroup AttemptToRemove(string guest, string persona = Personas.Organizer) =>
        new(guest, null, persona, mustSucceed: false);

    public static RemoveFromTheGroup AGuestWhoDoesNotExist() =>
        new(null, _ => Guid.NewGuid().ToString(), Personas.Organizer, mustSucceed: false);

    public static RemoveFromTheGroup TheOrganizerThemselves() =>
        new(null, actor => actor.Recall<OwnSession>().GuestSessionId, Personas.Organizer, mustSucceed: false);

    public async Task PerformAsAsync(Actor actor)
    {
        var booking = actor.Recall<GroupBooking>(Personas.Organizer);
        var sessionId = _guest is null ? _sessionId!(actor) : actor.Recall<GuestCart>(_guest).SessionId;
        var token = await Accounts.TokenOfAsync(actor, _persona);
        var response = actor.TheLastResponse();

        await response.CaptureAsync(await new GroupPreOrderApi(actor.UsingTheApi())
            .RemoveParticipantAsync(booking.GroupReservationId, sessionId, token));

        if (_mustSucceed && !response.Succeeded)
            throw new InvalidOperationException($"Removing {_guest} failed with {response.Status}: {response.Body}");
    }
}
