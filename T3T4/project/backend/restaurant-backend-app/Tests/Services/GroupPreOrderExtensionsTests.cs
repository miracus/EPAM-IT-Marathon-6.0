using Amazon.CognitoIdentityProvider.Model;
using APIHandler.Constants;
using APIHandler.Constants.Dishes;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.GuestSessions;
using APIHandler.Repositories.Auth;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Services.GuestSessions;
using Moq;

namespace APIHandler.Tests.Services;

/// <summary>Group pre-order extensions US13–US21 (owner side).</summary>
public class GroupPreOrderExtensionsTests
{
    private const string OwnerId = "owner-user";
    private const string GroupId = "group-1";
    private const string ReservationId = "reservation-1";

    // The reservation starts at 19:00 UTC; the default cut-off is 17:00 UTC.
    private static readonly DateTime Start = new(2026, 11, 7, 19, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IGroupReservationRepository> _groups = new();
    private readonly Mock<IGuestSessionRepository> _sessions = new();
    private readonly Mock<IDishRepository> _dishes = new();
    private readonly Mock<IReservationRepository> _reservations = new();
    private readonly Mock<IOrderRepository> _orders = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly FixedClock _clock = new(Start.AddHours(-5));
    private readonly GroupPreOrderService _service;

    private readonly GroupReservation _group;
    private readonly Reservation _reservation;
    private readonly Dish _salad = MakeDish("salad", "Spring Salad", 19m, DishType.Appetizer);
    private readonly Dish _soup = MakeDish("soup", "Tom Yum", 22m, DishType.MainCourse);
    private readonly Dish _cake = MakeDish("cake", "Berry Cheesecake", 9m, DishType.Dessert);
    private readonly Dish _stopped = MakeDish("stopped", "Asparagus", 12m, DishType.Appetizer, DishState.OnStop);

    public GroupPreOrderExtensionsTests()
    {
        _service = new GroupPreOrderService(
            _groups.Object, _sessions.Object, _dishes.Object, _reservations.Object, _orders.Object, _users.Object, _clock);

        _group = new GroupReservation
        {
            GroupReservationId = GroupId,
            ReservationId = ReservationId,
            ReservationToken = "token-1",
            Status = GroupReservationStatus.Open,
        };

        _reservation = new Reservation
        {
            Id = ReservationId,
            TableId = "table-1",
            WaiterId = "waiter-1",
            CustomerId = OwnerId,
            GuestCount = 5,
            Status = ReservationStatus.Reserved,
            StartTime = Start,
            EndTime = Start.AddMinutes(90),
        };

        _groups.Setup(r => r.GetGroupReservationByIdAsync(GroupId)).ReturnsAsync(() => _group);
        _groups.Setup(r => r.GetByReservationIdAsync(ReservationId)).ReturnsAsync(() => _group);
        _reservations.Setup(r => r.GetReservationByIdAsync(ReservationId)).ReturnsAsync(() => _reservation);

        var allDishes = new[] { _salad, _soup, _cake, _stopped };
        _dishes.Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> ids) => allDishes.Where(d => ids.Contains(d.Id)).ToList());
        _dishes.Setup(r => r.GetDishByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => allDishes.FirstOrDefault(d => d.Id == id));

        _users.Setup(r => r.GetUserByIdAsync(OwnerId)).ReturnsAsync(new UserType
        {
            Attributes = [new AttributeType { Name = "given_name", Value = "Oleh" }],
        });
    }

    // ------------------------------------------------------------------ US13: cut-off and auto-send

    [Fact]
    public async Task Snapshot_ShowsCutoffTwoHoursBeforeStart()
    {
        SetupSessions(Owner());

        GetAllSessionsResponse result = await _service.GetSessionsAsync(OwnerId, GroupId);

        Assert.Equal(Start.AddHours(-2), result.CutoffAt);
        Assert.Equal(3 * 3600, result.SecondsUntilCutoff);
        Assert.False(result.IsLocked);
        Assert.True(result.AutoRemind);
        Assert.Equal(5, result.MaxGuests);
        Assert.Equal("Reserved", result.ReservationStatus);
    }

    [Fact]
    public async Task Snapshot_AfterCutoff_SendsPreOrderAutomatically()
    {
        _clock.Now = Start.AddHours(-2);
        SetupSessions(
            Owner(),
            Guest("anna", "Anna", submitted: true, (_salad.Id, 2), (_stopped.Id, 1)),
            Guest("olena", "Olena", submitted: false, (_soup.Id, 1)));
        Order order = SetupOrder(new OrderItem { DishId = _cake.Id, Quantity = 1, State = OrderItemState.Pending });

        GetAllSessionsResponse result = await _service.GetSessionsAsync(OwnerId, GroupId);

        Assert.Equal(GroupReservationStatus.Submitted, _group.Status);
        Assert.True(_group.SubmittedAutomatically);
        Assert.Equal(_clock.Now, _group.SubmittedAt);
        Assert.Equal(OrderStatus.SubmittedByCustomer, order.Status);
        // Anna's salad is sent, the stopped dish and Olena's unsubmitted cart are not.
        Assert.Equal(2, order.Dishes.Single(d => d.DishId == _salad.Id).Quantity);
        Assert.DoesNotContain(order.Dishes, d => d.DishId == _stopped.Id || d.DishId == _soup.Id);
        Assert.True(result.IsLocked);
        Assert.True(result.SubmittedAutomatically);
        Assert.Equal("Submitted", result.GroupStatus);
    }

    [Fact]
    public async Task Snapshot_AfterCutoff_WithoutOrder_LocksTheGroup()
    {
        _clock.Now = Start.AddHours(-1);
        SetupSessions(Owner(), Guest("anna", "Anna", submitted: false));
        _orders.Setup(r => r.GetOrderByReservationIdAsync(ReservationId)).ReturnsAsync((Order?)null);

        GetAllSessionsResponse result = await _service.GetSessionsAsync(OwnerId, GroupId);

        Assert.Equal(GroupReservationStatus.Submitted, _group.Status);
        Assert.True(result.IsLocked);
        _orders.Verify(r => r.SaveOrderAsync(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task Submit_AfterCutoff_Returns409BecauseItWasSentAutomatically()
    {
        _clock.Now = Start.AddHours(-1);
        SetupSessions(Owner(), Guest("anna", "Anna", submitted: true, (_salad.Id, 1)));
        SetupOrder();

        await Assert.ThrowsAsync<GroupReservationSubmittedException>(
            () => _service.SubmitAsync(OwnerId, GroupId, ReservationId));
        Assert.True(_group.SubmittedAutomatically);
    }

    [Fact]
    public async Task Submit_BeforeCutoff_RecordsTimeAndManualMode()
    {
        SetupSessions(Owner(), Guest("anna", "Anna", submitted: true, (_salad.Id, 1)));
        SetupOrder();

        await _service.SubmitAsync(OwnerId, GroupId, ReservationId);

        Assert.Equal(_clock.Now, _group.SubmittedAt);
        Assert.False(_group.SubmittedAutomatically);
    }

    [Fact]
    public async Task OwnerEdits_AfterCutoff_AreRejected()
    {
        _clock.Now = Start.AddHours(-1);
        SetupSessions(Owner(), Guest("anna", "Anna", submitted: true, (_salad.Id, 1)));
        SetupOrder();

        await Assert.ThrowsAsync<GroupReservationSubmittedException>(
            () => _service.RemoveGuestDishAsync(OwnerId, GroupId, "anna", _salad.Id));
    }

    // ------------------------------------------------------------------ US14: reminders

    [Fact]
    public async Task Remind_All_RemindsOnlyGuestsWithoutDishes()
    {
        GuestSession iryna = Guest("iryna", "Iryna", submitted: false);
        GuestSession anna = Guest("anna", "Anna", submitted: false, (_salad.Id, 1));
        SetupSessions(Owner(), iryna, anna);

        RemindersResponse result = await _service.SendRemindersAsync(OwnerId, GroupId, new RemindRequest());

        ReminderResult reminded = Assert.Single(result.Reminded);
        Assert.Equal("iryna", reminded.Id);
        Assert.Empty(result.Skipped);
        GuestNotice notice = Assert.Single(iryna.Notices!);
        Assert.Equal(GuestNoticeTypes.Reminder, notice.Type);
        Assert.Equal("Oleh", notice.ActorName);
        Assert.Equal(_clock.Now, iryna.LastRemindedAt);
        Assert.Null(anna.Notices);
    }

    [Fact]
    public async Task Remind_SameGuestWithin15Minutes_IsSkipped()
    {
        GuestSession iryna = Guest("iryna", "Iryna", submitted: false);
        iryna.LastRemindedAt = _clock.Now.AddMinutes(-10);
        SetupSessions(Owner(), iryna);

        RemindersResponse result = await _service.SendRemindersAsync(OwnerId, GroupId, new RemindRequest { GuestSessionIds = ["iryna"] });

        Assert.Empty(result.Reminded);
        ReminderResult skipped = Assert.Single(result.Skipped);
        Assert.Equal(_clock.Now.AddMinutes(-10), skipped.LastRemindedAt);
        Assert.Null(iryna.Notices);
    }

    [Fact]
    public async Task Remind_AfterCooldown_RemindsAgain()
    {
        GuestSession iryna = Guest("iryna", "Iryna", submitted: false);
        iryna.LastRemindedAt = _clock.Now.AddMinutes(-16);
        SetupSessions(Owner(), iryna);

        RemindersResponse result = await _service.SendRemindersAsync(OwnerId, GroupId, new RemindRequest { GuestSessionIds = ["iryna"] });

        Assert.Single(result.Reminded);
    }

    [Fact]
    public async Task AutoRemind_ThirtyMinutesBeforeCutoff_RemindsOnce()
    {
        _clock.Now = Start.AddHours(-2).AddMinutes(-20);
        GuestSession iryna = Guest("iryna", "Iryna", submitted: false);
        SetupSessions(Owner(), iryna, Guest("anna", "Anna", submitted: true, (_salad.Id, 1)));

        await _service.GetSessionsAsync(OwnerId, GroupId);
        await _service.GetSessionsAsync(OwnerId, GroupId);

        Assert.Equal(_clock.Now, _group.AutoRemindedAt);
        Assert.Single(iryna.Notices!);
    }

    [Fact]
    public async Task AutoRemind_Disabled_SendsNothing()
    {
        _clock.Now = Start.AddHours(-2).AddMinutes(-20);
        GuestSession iryna = Guest("iryna", "Iryna", submitted: false);
        SetupSessions(Owner(), iryna);

        GetAllSessionsResponse result = await _service.UpdateSettingsAsync(OwnerId, GroupId, new GroupSettingsRequest { AutoRemind = false });

        Assert.True(_group.AutoRemindDisabled);
        Assert.False(result.AutoRemind);
        Assert.Null(iryna.Notices);
    }

    // ------------------------------------------------------------------ US15/US16: owner edits

    [Fact]
    public async Task RemoveGuestDish_NoticeHasDishOldAndNewTotal()
    {
        GuestSession anna = Guest("anna", "Anna", submitted: true, (_salad.Id, 2), (_soup.Id, 1));
        SetupSessions(Owner(), anna);
        SetupOrder();

        GetAllSessionsResponse result = await _service.RemoveGuestDishAsync(OwnerId, GroupId, "anna", _soup.Id);

        GuestNotice notice = Assert.Single(anna.Notices!);
        Assert.Equal(GuestNoticeTypes.DishRemoved, notice.Type);
        Assert.Equal("Tom Yum", notice.DishName);
        Assert.Equal(1, notice.Quantity);
        Assert.Equal(60m, notice.OldTotal);
        Assert.Equal(38m, notice.NewTotal);
        Assert.Equal("Oleh removed Tom Yum from your order.", notice.Message);
        Assert.True(anna.ChangedByOwner);
        Assert.True(result.Participants.Single(p => p.Id == "anna").ChangedByOwner);
    }

    [Fact]
    public async Task RestoreGuestDish_UndoPutsItBackAndClearsTheNotice()
    {
        GuestSession anna = Guest("anna", "Anna", submitted: true, (_salad.Id, 2), (_soup.Id, 1));
        SetupSessions(Owner(), anna);
        SetupOrder();
        await _service.RemoveGuestDishAsync(OwnerId, GroupId, "anna", _soup.Id);

        GetAllSessionsResponse result = await _service.RestoreGuestDishAsync(
            OwnerId, GroupId, "anna", _soup.Id, new RestoreDishRequest { Quantity = 1, Note = "no chili" });

        Assert.Equal(1, anna.Dishes.Single(d => d.DishId == _soup.Id).Quantity);
        Assert.Equal("no chili", anna.Dishes.Single(d => d.DishId == _soup.Id).Note);
        Assert.Empty(anna.Notices!);
        Assert.False(anna.ChangedByOwner);
        Assert.Equal(60m, result.Participants.Single(p => p.Id == "anna").Total);
    }

    [Fact]
    public async Task RestoreGuestDish_ZeroQuantity_IsRejected()
    {
        SetupSessions(Owner(), Guest("anna", "Anna", submitted: true));

        await Assert.ThrowsAsync<ValidationException>(() => _service.RestoreGuestDishAsync(
            OwnerId, GroupId, "anna", _soup.Id, new RestoreDishRequest { Quantity = 0 }));
    }

    [Fact]
    public async Task RequestChange_ReopensTheGuestCart()
    {
        GuestSession anna = Guest("anna", "Anna", submitted: true, (_salad.Id, 5));
        SetupSessions(Owner(), anna);
        SetupOrder();

        await _service.RequestChangeAsync(OwnerId, GroupId, "anna");

        Assert.False(anna.IsSubmitted);
        Assert.Equal(GuestNoticeTypes.ChangeRequested, Assert.Single(anna.Notices!).Type);
    }

    [Fact]
    public async Task DeleteSession_RemembersRemovedGuest()
    {
        SetupSessions(Owner(), Guest("anna", "Anna", submitted: false));
        SetupOrder();

        await _service.DeleteSessionAsync(OwnerId, GroupId, "anna");

        Assert.Contains("anna", _group.RemovedSessionIds!);
        _groups.Verify(r => r.SaveGroupReservationAsync(_group), Times.Once);
    }

    // ------------------------------------------------------------------ US18/US19: notes, allergies, serving time

    [Fact]
    public async Task Snapshot_ItemsCarryNoteServeTimeAndAllergies()
    {
        GuestSession anna = Guest("anna", "Anna", submitted: true, (_salad.Id, 1), (_cake.Id, 1));
        anna.Dishes[0].Note = "no onion";
        anna.Allergies = "Nuts";
        SetupSessions(Owner(), anna);
        SetupOrder();

        GetAllSessionsResponse result = await _service.GetSessionsAsync(OwnerId, GroupId);

        Participant card = result.Participants.Single(p => p.Id == "anna");
        Assert.Equal("Nuts", card.Allergies);
        Assert.Equal("no onion", card.Items.Single(i => i.Dish.Id == _salad.Id).Note);
        Assert.Equal(ServeTimes.OnArrival, card.Items.Single(i => i.Dish.Id == _salad.Id).ServeTime);
        // US19 AC2: desserts default to "After mains".
        Assert.Equal(ServeTimes.AfterMains, card.Items.Single(i => i.Dish.Id == _cake.Id).ServeTime);
    }

    [Fact]
    public async Task UpdateOwnerDish_SetsNoteAndServeTimeOnTheOrder()
    {
        SetupSessions(Owner());
        Order order = SetupOrder(new OrderItem { DishId = _soup.Id, Quantity = 1, State = OrderItemState.Pending });

        GetAllSessionsResponse result = await _service.UpdateOwnerDishAsync(
            OwnerId, GroupId, _soup.Id, new DishDetailsRequest { Note = "  extra lime  ", ServeTime = ServeTimes.AfterMains });

        OrderItem item = Assert.Single(order.Dishes);
        Assert.Equal("extra lime", item.Note);
        Assert.Equal(ServeTimes.AfterMains, item.ServeTime);
        Assert.Equal("extra lime", result.Participants.Single(p => p.Type == ParticipantType.Owner).Items.Single().Note);
    }

    [Theory]
    [InlineData(81, null)]
    [InlineData(10, "Later")]
    public async Task UpdateOwnerDish_InvalidInput_IsRejected(int noteLength, string? serveTime)
    {
        SetupSessions(Owner());
        SetupOrder(new OrderItem { DishId = _soup.Id, Quantity = 1, State = OrderItemState.Pending });

        await Assert.ThrowsAsync<ValidationException>(() => _service.UpdateOwnerDishAsync(
            OwnerId, GroupId, _soup.Id, new DishDetailsRequest { Note = new string('a', noteLength), ServeTime = serveTime }));
    }

    [Fact]
    public async Task UpdateOwnerAllergies_SavesOnOwnerSession()
    {
        GuestSession owner = Owner();
        SetupSessions(owner);
        SetupOrder();

        GetAllSessionsResponse result = await _service.UpdateOwnerAllergiesAsync(OwnerId, GroupId, new AllergiesRequest { Allergies = "Gluten" });

        Assert.Equal("Gluten", owner.Allergies);
        Assert.Equal("Gluten", result.Participants.Single(p => p.Type == ParticipantType.Owner).Allergies);
    }

    [Fact]
    public async Task Submit_SendsNotesServeTimesAndAllergiesToTheKitchen()
    {
        GuestSession owner = Owner();
        owner.Allergies = "Gluten";
        GuestSession anna = Guest("anna", "Anna", submitted: true, (_cake.Id, 1));
        anna.Dishes[0].Note = "candle please";
        anna.Allergies = "Nuts";
        SetupSessions(owner, anna);
        Order order = SetupOrder(new OrderItem { DishId = _soup.Id, Quantity = 1, State = OrderItemState.Pending });

        await _service.SubmitAsync(OwnerId, GroupId, ReservationId);

        KitchenNote cake = order.KitchenNotes!.Single(n => n.DishId == _cake.Id);
        Assert.Equal("Anna", cake.ParticipantName);
        Assert.Equal("candle please", cake.Note);
        Assert.Equal(ServeTimes.AfterMains, cake.ServeTime);
        Assert.Equal("Oleh", order.KitchenNotes!.Single(n => n.DishId == _soup.Id).ParticipantName);
        Assert.Contains(order.Allergies!, a => a.ParticipantName == "Anna" && a.Allergies == "Nuts");
        Assert.Contains(order.Allergies!, a => a.ParticipantName == "Oleh" && a.Allergies == "Gluten");
    }

    // ------------------------------------------------------------------ US17: split the bill

    [Fact]
    public void SplitEqually_RoundingDifferenceGoesToTheOrganizer()
    {
        decimal[] shares = GroupPreOrderService.SplitEqually(100m, 3);

        Assert.Equal([33.34m, 33.33m, 33.33m], shares);
        Assert.Equal(100m, shares.Sum());
    }

    [Fact]
    public async Task Payment_BeforeSubmit_IsRejected()
    {
        SetupSessions(Owner());

        await Assert.ThrowsAsync<ValidationException>(() => _service.GetPaymentAsync(OwnerId, GroupId));
    }

    [Fact]
    public async Task Payment_EachPaysOwn_PaidAndPendingAddUpToGrandTotal()
    {
        SetupSubmittedGroup(out GuestSession owner, out GuestSession anna, out _);

        PaymentView view = await _service.SetPaymentModeAsync(OwnerId, GroupId, PaymentModes.EachPaysOwn);
        Assert.Equal(9m + 38m + 22m, view.GrandTotal);
        Assert.Equal(38m, view.Participants.Single(p => p.Id == "anna").Amount);
        Assert.Equal(0m, view.PaidTotal);

        view = await _service.MarkPaidAsync(OwnerId, GroupId, "anna");

        Assert.True(anna.IsPaid);
        Assert.Equal(38m, view.PaidTotal);
        Assert.Equal(view.GrandTotal, view.PaidTotal + view.PendingTotal);
    }

    [Fact]
    public async Task Payment_OwnerPaysAll_GuestsOweNothing()
    {
        SetupSubmittedGroup(out _, out _, out _);

        PaymentView view = await _service.SetPaymentModeAsync(OwnerId, GroupId, PaymentModes.OwnerPaysAll);

        Assert.Equal(69m, view.Participants.Single(p => p.Type == ParticipantType.Owner).Amount);
        Assert.All(view.Participants.Where(p => p.Type == ParticipantType.Guest), p =>
        {
            Assert.Equal(0m, p.Amount);
            Assert.True(p.IsPaid);
        });
        Assert.Equal(69m, view.PendingTotal);
    }

    [Fact]
    public async Task Payment_SplitEqually_UsesEveryoneWhoseCartWasSent()
    {
        SetupSubmittedGroup(out _, out _, out _);

        PaymentView view = await _service.SetPaymentModeAsync(OwnerId, GroupId, PaymentModes.SplitEqually);

        Assert.Equal(3, view.Participants.Count);
        Assert.Equal(23m, view.Participants.Single(p => p.Id == "anna").Amount);
        Assert.Equal(69m, view.Participants.Sum(p => p.Amount));
    }

    [Fact]
    public async Task Payment_InvalidMode_IsRejected()
    {
        SetupSubmittedGroup(out _, out _, out _);

        await Assert.ThrowsAsync<ValidationException>(() => _service.SetPaymentModeAsync(OwnerId, GroupId, "Crypto"));
    }

    [Fact]
    public async Task SendPaymentLinks_NotifiesPendingGuests()
    {
        SetupSubmittedGroup(out _, out GuestSession anna, out GuestSession olena);
        _group.PaymentMode = PaymentModes.EachPaysOwn;
        olena.IsPaid = true;

        await _service.SendPaymentLinksAsync(OwnerId, GroupId);

        GuestNotice notice = Assert.Single(anna.Notices!);
        Assert.Equal(GuestNoticeTypes.PaymentRequested, notice.Type);
        Assert.Equal(38m, notice.Amount);
        Assert.Equal(_clock.Now, anna.PaymentLinkSentAt);
        Assert.Null(olena.Notices);
    }

    [Fact]
    public async Task PayGuestShare_MarksTheGuestAsPaid()
    {
        SetupSubmittedGroup(out _, out GuestSession anna, out _);
        _group.PaymentMode = PaymentModes.SplitEqually;

        GuestPaymentShare share = await _service.PayGuestShareAsync(GroupId, "anna");

        Assert.True(share.IsPaid);
        Assert.Equal(23m, share.Amount);
        Assert.True(anna.IsPaid);
    }

    [Fact]
    public async Task Remind_AtPaymentStep_RemindsUnpaidGuests()
    {
        SetupSubmittedGroup(out _, out GuestSession anna, out GuestSession olena);
        _group.PaymentMode = PaymentModes.EachPaysOwn;
        olena.IsPaid = true;

        RemindersResponse result = await _service.SendRemindersAsync(OwnerId, GroupId, new RemindRequest());

        Assert.Equal("anna", Assert.Single(result.Reminded).Id);
        Assert.Equal(GuestNoticeTypes.PaymentRequested, Assert.Single(anna.Notices!).Type);
    }

    // ------------------------------------------------------------------ US21: book again

    [Fact]
    public async Task Rebook_LinksTheNewGroupToThePreviousOne()
    {
        const string newReservationId = "reservation-2";
        var newGroup = new GroupReservation
        {
            GroupReservationId = "group-2",
            ReservationId = newReservationId,
            ReservationToken = "token-2",
            Status = GroupReservationStatus.Open,
        };
        _reservation.Status = ReservationStatus.Completed;
        _reservations.Setup(r => r.GetReservationByIdAsync(newReservationId)).ReturnsAsync(new Reservation
        {
            Id = newReservationId, TableId = "table-1", WaiterId = "waiter-1", CustomerId = OwnerId, GuestCount = 5,
            Status = ReservationStatus.Reserved, StartTime = Start.AddDays(7),
        });
        _groups.Setup(r => r.GetByReservationIdAsync(newReservationId)).ReturnsAsync(newGroup);
        SetupSessions(Owner(), Guest("anna", "Anna", submitted: true), Guest("olena", "Olena", submitted: true));

        RebookResponse result = await _service.RebookAsync(OwnerId, newReservationId, ReservationId);

        Assert.Equal("token-2", result.InviteToken);
        Assert.Equal(2, result.InvitedParticipants);
        Assert.Equal(ReservationId, newGroup.PreviousReservationId);
        Assert.Equal(newReservationId, _group.NextReservationId);
    }

    [Fact]
    public async Task Rebook_FromCancelledReservation_IsRejected()
    {
        _reservation.Status = ReservationStatus.Cancelled;
        _reservations.Setup(r => r.GetReservationByIdAsync("reservation-2")).ReturnsAsync(new Reservation
        {
            Id = "reservation-2", TableId = "table-1", WaiterId = "waiter-1", CustomerId = OwnerId, GuestCount = 5,
        });

        await Assert.ThrowsAsync<ValidationException>(() => _service.RebookAsync(OwnerId, "reservation-2", ReservationId));
    }

    [Fact]
    public async Task Rebook_SomeoneElsesReservation_IsForbidden()
    {
        _reservations.Setup(r => r.GetReservationByIdAsync("reservation-2")).ReturnsAsync(new Reservation
        {
            Id = "reservation-2", TableId = "table-1", WaiterId = "waiter-1", CustomerId = "someone-else", GuestCount = 5,
        });

        await Assert.ThrowsAsync<ActionForbiddenException>(() => _service.RebookAsync(OwnerId, "reservation-2", ReservationId));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Owner (cheesecake $9), Anna (2 × salad = $38), Olena (soup $22) — sent to the restaurant.</summary>
    private void SetupSubmittedGroup(out GuestSession owner, out GuestSession anna, out GuestSession olena)
    {
        owner = Owner((_cake.Id, 1));
        owner.IsSubmitted = true;
        anna = Guest("anna", "Anna", submitted: true, (_salad.Id, 2));
        olena = Guest("olena", "Olena", submitted: true, (_soup.Id, 1));
        SetupSessions(owner, anna, olena, Guest("maks", "Maksym", submitted: false, (_salad.Id, 1)));
        _group.Status = GroupReservationStatus.Submitted;
    }

    private static Dish MakeDish(string id, string name, decimal price, DishType type, DishState state = DishState.Available) => new()
    {
        Id = id,
        Name = name,
        Description = name,
        DishType = type,
        Price = price,
        Weight = "300 g",
        Calories = "200",
        Proteins = "1",
        Fats = "1",
        Carbohydrates = "1",
        Vitamins = "A",
        State = state,
    };

    private static GuestSession Guest(string id, string name, bool submitted, params (string DishId, int Quantity)[] dishes) => new()
    {
        Id = id,
        GroupReservationId = GroupId,
        GuestName = name,
        IsSubmitted = submitted,
        Dishes = dishes.Select(d => new OrderItem { DishId = d.DishId, Quantity = d.Quantity, State = OrderItemState.Pending }).ToList(),
    };

    private static GuestSession Owner(params (string DishId, int Quantity)[] dishes)
    {
        GuestSession session = Guest("owner-session", "Owner", submitted: false, dishes);
        session.IsOwnerSession = true;
        session.OwnerUserId = OwnerId;
        return session;
    }

    private void SetupSessions(params GuestSession[] sessions)
    {
        _sessions.Setup(r => r.GetAllByGroupReservationIdAsync(GroupId)).ReturnsAsync(() => sessions.ToList());
        foreach (GuestSession session in sessions)
            _sessions.Setup(r => r.GetByGuestSessionId(session.Id)).ReturnsAsync(session);
    }

    private Order SetupOrder(params OrderItem[] existing)
    {
        var order = new Order
        {
            OrderId = "order-1",
            CustomerId = OwnerId,
            ReservationId = ReservationId,
            ReservationAddress = "Street 1, Table 1",
            ReservationTime = "Nov 7, 2026",
            Dishes = existing.ToList(),
            Status = OrderStatus.Created,
        };
        _orders.Setup(r => r.GetOrderByReservationIdAsync(ReservationId)).ReturnsAsync(order);
        return order;
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public DateTime Now { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }
}
