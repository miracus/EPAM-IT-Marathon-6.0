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

/// <summary>
/// Owner-side group pre-order: homework L1–L3 and the US12/US13/US15/US16 extensions.
/// </summary>
public class GroupPreOrderServiceTests
{
    private const string OwnerId = "owner-user";
    private const string OtherUserId = "someone-else";
    private const string GroupId = "group-1";
    private const string ReservationId = "reservation-1";

    private readonly Mock<IGroupReservationRepository> _groups = new();
    private readonly Mock<IGuestSessionRepository> _sessions = new();
    private readonly Mock<IDishRepository> _dishes = new();
    private readonly Mock<IReservationRepository> _reservations = new();
    private readonly Mock<IOrderRepository> _orders = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly GroupPreOrderService _service;

    private readonly GroupReservation _group;
    private readonly Reservation _reservation;
    private readonly Dish _salad = MakeDish("salad", "Spring Salad", 19m);
    private readonly Dish _soup = MakeDish("soup", "Tom Yum", 22m);
    private readonly Dish _stopped = MakeDish("stopped", "Asparagus", 12m, DishState.OnStop);

    public GroupPreOrderServiceTests()
    {
        _service = new GroupPreOrderService(
            _groups.Object, _sessions.Object, _dishes.Object, _reservations.Object, _orders.Object, _users.Object);

        _group = new GroupReservation
        {
            GroupReservationId = GroupId,
            ReservationId = ReservationId,
            ReservationToken = "token",
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
        };

        _groups.Setup(r => r.GetGroupReservationByIdAsync(GroupId)).ReturnsAsync(() => _group);
        _groups.Setup(r => r.GetByReservationIdAsync(ReservationId)).ReturnsAsync(() => _group);
        _reservations.Setup(r => r.GetReservationByIdAsync(ReservationId)).ReturnsAsync(() => _reservation);

        var allDishes = new[] { _salad, _soup, _stopped };
        _dishes.Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> ids) => allDishes.Where(d => ids.Contains(d.Id)).ToList());
        _dishes.Setup(r => r.GetDishByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => allDishes.FirstOrDefault(d => d.Id == id));

        _users.Setup(r => r.GetUserByIdAsync(OwnerId)).ReturnsAsync(new UserType
        {
            Attributes = [new AttributeType { Name = "given_name", Value = "Oleh" }],
        });
    }

    // ------------------------------------------------------------------ L1: GET sessions

    [Fact]
    public async Task GetSessionsAsync_ReturnsParticipantsWithTotals_OwnerFirst()
    {
        SetupSessions(
            Guest("anna", "Anna", submitted: true, (_salad.Id, 2)),
            Owner("owner-session", (_soup.Id, 1)),
            Guest("oleh2", "Olena", submitted: false, (_soup.Id, 1)));

        GetAllSessionsResponse result = await _service.GetSessionsAsync(OwnerId, GroupId);

        var participants = result.Participants.ToList();
        Assert.Equal(3, participants.Count);

        Assert.Equal(ParticipantType.Owner, participants[0].Type);
        Assert.Equal("Oleh", participants[0].Name);
        Assert.Equal(22m, participants[0].Total);

        Participant anna = participants.Single(p => p.Id == "anna");
        Assert.Equal(ParticipantType.Guest, anna.Type);
        Assert.Equal(38m, anna.Total);
        Assert.True(anna.IsSubmitted);
        Assert.Equal(2, anna.Items.Single().Quantity);
        Assert.Equal("Spring Salad", anna.Items.Single().Dish.Name);

        Assert.Equal(38m + 22m + 22m, result.Total);
        Assert.Equal("Open", result.GroupStatus);
    }

    [Fact]
    public async Task GetSessionsAsync_UnavailableDish_ShownButExcludedFromTotals()
    {
        SetupSessions(Guest("max", "Maksym", submitted: false, (_salad.Id, 1), (_stopped.Id, 1)));

        GetAllSessionsResponse result = await _service.GetSessionsAsync(OwnerId, GroupId);

        Participant max = result.Participants.Single();
        Assert.Equal(2, max.Items.Count());
        Assert.Equal(19m, max.Total);
        Assert.Equal(19m, result.Total);
        Assert.True(result.HasUnavailableDishes);

        DishSummary stopped = result.SummaryByDish.Single(s => s.DishId == _stopped.Id);
        Assert.False(stopped.IsAvailable);
        Assert.Equal(0m, stopped.Total);
    }

    [Fact]
    public async Task GetSessionsAsync_SummaryByDish_GroupsIdenticalDishes()
    {
        SetupSessions(
            Guest("a", "Anna", submitted: true, (_salad.Id, 2)),
            Guest("b", "Olena", submitted: true, (_salad.Id, 1), (_soup.Id, 1)));

        GetAllSessionsResponse result = await _service.GetSessionsAsync(OwnerId, GroupId);

        DishSummary salad = result.SummaryByDish.Single(s => s.DishId == _salad.Id);
        Assert.Equal(3, salad.Quantity);
        Assert.Equal(57m, salad.Total);
        Assert.Equal(2, result.SummaryByDish.Count());
        Assert.False(result.HasUnavailableDishes);
    }

    [Fact]
    public async Task GetSessionsAsync_EmptyGroup_ReturnsZeroTotal()
    {
        SetupSessions();

        GetAllSessionsResponse result = await _service.GetSessionsAsync(OwnerId, GroupId);

        Assert.Empty(result.Participants);
        Assert.Equal(0m, result.Total);
        _dishes.Verify(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task GetSessionsAsync_DishDeletedFromMenu_IsSkipped()
    {
        SetupSessions(Guest("a", "Anna", submitted: false, (_salad.Id, 1), ("deleted-dish", 3)));

        GetAllSessionsResponse result = await _service.GetSessionsAsync(OwnerId, GroupId);

        Assert.Single(result.Participants.Single().Items);
        Assert.Equal(19m, result.Total);
    }

    [Fact]
    public async Task GetSessionsAsync_NotOwner_Throws403()
    {
        SetupSessions();
        await Assert.ThrowsAsync<ActionForbiddenException>(() => _service.GetSessionsAsync(OtherUserId, GroupId));
    }

    [Fact]
    public async Task GetSessionsAsync_GroupNotFound_Throws404()
    {
        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _service.GetSessionsAsync(OwnerId, "missing"));
    }

    [Fact]
    public async Task GetSessionsAsync_OwnerNameFromCognitoFails_FallsBackToOwner()
    {
        _users.Setup(r => r.GetUserByIdAsync(OwnerId)).ThrowsAsync(new InvalidOperationException("cognito down"));
        SetupSessions(Owner("owner-session"));

        GetAllSessionsResponse result = await _service.GetSessionsAsync(OwnerId, GroupId);

        Assert.Equal("Owner", result.Participants.Single().Name);
    }

    // ------------------------------------------------------------------ L2: owner session id

    [Fact]
    public async Task GetOwnerSessionIdAsync_ExistingOwnerSession_ReturnsIt()
    {
        SetupSessions(Guest("a", "Anna", submitted: false), Owner("owner-session"));

        OwnerSessionIdResponse result = await _service.GetOwnerSessionIdAsync(OwnerId, ReservationId);

        Assert.Equal("owner-session", result.GuestSessionId);
        _sessions.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task GetOwnerSessionIdAsync_NoOwnerSession_CreatesOne()
    {
        SetupSessions(Guest("a", "Anna", submitted: false));
        GuestSession? saved = null;
        _sessions.Setup(r => r.SaveAsync(It.IsAny<GuestSession>())).Callback<GuestSession>(s => saved = s).Returns(Task.CompletedTask);

        OwnerSessionIdResponse result = await _service.GetOwnerSessionIdAsync(OwnerId, ReservationId);

        Assert.NotNull(saved);
        Assert.True(saved!.IsOwnerSession);
        Assert.Equal(OwnerId, saved.OwnerUserId);
        Assert.Equal(GroupId, saved.GroupReservationId);
        Assert.Equal(saved.Id, result.GuestSessionId);
    }

    [Fact]
    public async Task GetOwnerSessionIdAsync_NotOwner_Throws403()
    {
        await Assert.ThrowsAsync<ActionForbiddenException>(() => _service.GetOwnerSessionIdAsync(OtherUserId, ReservationId));
    }

    [Fact]
    public async Task GetOwnerSessionIdAsync_ReservationNotFound_Throws404()
    {
        await Assert.ThrowsAsync<ReservationNotFoundException>(() => _service.GetOwnerSessionIdAsync(OwnerId, "missing"));
    }

    [Fact]
    public async Task GetOwnerSessionIdAsync_NoGroupForReservation_Throws404()
    {
        _groups.Setup(r => r.GetByReservationIdAsync(ReservationId)).ReturnsAsync((GroupReservation?)null);
        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _service.GetOwnerSessionIdAsync(OwnerId, ReservationId));
    }

    // ------------------------------------------------------------------ L2: submit

    [Fact]
    public async Task SubmitAsync_MergesOnlySubmittedSessions_ByDishId()
    {
        SetupSessions(
            Guest("a", "Anna", submitted: true, (_salad.Id, 2)),
            Guest("b", "Olena", submitted: true, (_salad.Id, 1), (_soup.Id, 2)),
            Guest("c", "Iryna", submitted: false, (_soup.Id, 5)));
        Order order = SetupOrder();

        IReadOnlyList<SubmittedDishResponse> result = await _service.SubmitAsync(OwnerId, GroupId, ReservationId);

        Assert.Equal(2, result.Count);
        Assert.Equal(3, result.Single(r => r.DishId == _salad.Id).Quantity);
        Assert.Equal(2, result.Single(r => r.DishId == _soup.Id).Quantity);

        Assert.Equal(OrderStatus.SubmittedByCustomer, order.Status);
        Assert.Equal(3, order.Dishes.Single(d => d.DishId == _salad.Id).Quantity);
        Assert.Equal(2, order.Dishes.Single(d => d.DishId == _soup.Id).Quantity);
        Assert.All(order.Dishes, d => Assert.Equal(OrderItemState.Pending, d.State));
        _orders.Verify(r => r.SaveOrderAsync(order), Times.Once);

        Assert.Equal(GroupReservationStatus.Submitted, _group.Status);
        _groups.Verify(r => r.SaveGroupReservationAsync(_group), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_ExistingOrderDish_QuantityIsAdded()
    {
        SetupSessions(Guest("a", "Anna", submitted: true, (_salad.Id, 2)));
        Order order = SetupOrder(new OrderItem { DishId = _salad.Id, Quantity = 1, State = OrderItemState.Pending });

        await _service.SubmitAsync(OwnerId, GroupId, ReservationId);

        Assert.Equal(3, order.Dishes.Single().Quantity);
    }

    [Fact]
    public async Task SubmitAsync_UnavailableDishes_AreNotSent()
    {
        SetupSessions(Guest("a", "Anna", submitted: true, (_salad.Id, 1), (_stopped.Id, 1)));
        Order order = SetupOrder();

        IReadOnlyList<SubmittedDishResponse> result = await _service.SubmitAsync(OwnerId, GroupId, ReservationId);

        Assert.Equal(_salad.Id, Assert.Single(result).DishId);
        Assert.DoesNotContain(order.Dishes, d => d.DishId == _stopped.Id);
    }

    [Fact]
    public async Task SubmitAsync_NoSubmittedSessions_Throws400()
    {
        SetupSessions(Guest("a", "Anna", submitted: false, (_salad.Id, 1)));
        SetupOrder();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitAsync(OwnerId, GroupId, ReservationId));
        Assert.Contains("no submitted", ex.Message, StringComparison.OrdinalIgnoreCase);
        _orders.Verify(r => r.SaveOrderAsync(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task SubmitAsync_SubmittedSessionsWithoutDishes_Throws400()
    {
        SetupSessions(Guest("a", "Anna", submitted: true));
        SetupOrder();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitAsync(OwnerId, GroupId, ReservationId));
        Assert.Contains("do not contain any dishes", ex.Message);
    }

    [Fact]
    public async Task SubmitAsync_OnlyUnavailableDishes_Throws400()
    {
        SetupSessions(Guest("a", "Anna", submitted: true, (_stopped.Id, 2)));
        SetupOrder();

        await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitAsync(OwnerId, GroupId, ReservationId));
    }

    [Fact]
    public async Task SubmitAsync_OrderNotFound_Throws400()
    {
        SetupSessions(Guest("a", "Anna", submitted: true, (_salad.Id, 1)));
        _orders.Setup(r => r.GetOrderByReservationIdAsync(ReservationId)).ReturnsAsync((Order?)null);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitAsync(OwnerId, GroupId, ReservationId));
        Assert.Contains("Order", ex.Message);
        Assert.Equal(GroupReservationStatus.Open, _group.Status);
    }

    [Fact]
    public async Task SubmitAsync_GroupNotFound_Throws404()
    {
        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _service.SubmitAsync(OwnerId, "missing", ReservationId));
    }

    [Fact]
    public async Task SubmitAsync_ReservationOfAnotherGroup_Throws400()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitAsync(OwnerId, GroupId, "other-reservation"));
        Assert.Contains("does not belong", ex.Message);
    }

    [Fact]
    public async Task SubmitAsync_ReservationNotFound_Throws404()
    {
        _reservations.Setup(r => r.GetReservationByIdAsync(ReservationId)).ReturnsAsync((Reservation?)null);
        await Assert.ThrowsAsync<ReservationNotFoundException>(() => _service.SubmitAsync(OwnerId, GroupId, ReservationId));
    }

    [Fact]
    public async Task SubmitAsync_NotOwner_Throws403()
    {
        await Assert.ThrowsAsync<ActionForbiddenException>(() => _service.SubmitAsync(OtherUserId, GroupId, ReservationId));
    }

    [Fact]
    public async Task SubmitAsync_AlreadySubmitted_Throws409()
    {
        _group.Status = GroupReservationStatus.Submitted;
        await Assert.ThrowsAsync<GroupReservationSubmittedException>(() => _service.SubmitAsync(OwnerId, GroupId, ReservationId));
    }

    // ------------------------------------------------------------------ L3: delete session

    [Fact]
    public async Task DeleteSessionAsync_RemovesGuest_ReturnsUpdatedSnapshot()
    {
        GuestSession anna = Guest("anna", "Anna", submitted: true, (_salad.Id, 2));
        GuestSession olena = Guest("olena", "Olena", submitted: false, (_soup.Id, 1));
        var stored = new List<GuestSession> { anna, olena };
        _sessions.Setup(r => r.GetByGuestSessionId("anna")).ReturnsAsync(anna);
        _sessions.Setup(r => r.GetAllByGroupReservationIdAsync(GroupId)).ReturnsAsync(() => stored.ToList());
        _sessions.Setup(r => r.DeleteAsync(anna)).Callback(() => stored.Remove(anna)).Returns(Task.CompletedTask);

        GetAllSessionsResponse result = await _service.DeleteSessionAsync(OwnerId, GroupId, "anna");

        _sessions.Verify(r => r.DeleteAsync(anna), Times.Once);
        Participant remaining = Assert.Single(result.Participants);
        Assert.Equal("olena", remaining.Id);
        Assert.Equal(22m, result.Total);
    }

    [Fact]
    public async Task DeleteSessionAsync_StaleIndexStillReturnsDeleted_IsExcluded()
    {
        GuestSession anna = Guest("anna", "Anna", submitted: true, (_salad.Id, 2));
        _sessions.Setup(r => r.GetByGuestSessionId("anna")).ReturnsAsync(anna);
        _sessions.Setup(r => r.GetAllByGroupReservationIdAsync(GroupId)).ReturnsAsync(new List<GuestSession> { anna });

        GetAllSessionsResponse result = await _service.DeleteSessionAsync(OwnerId, GroupId, "anna");

        Assert.Empty(result.Participants);
        Assert.Equal(0m, result.Total);
    }

    [Fact]
    public async Task DeleteSessionAsync_SessionOfAnotherGroup_Throws400()
    {
        GuestSession foreign = Guest("x", "X", submitted: false);
        foreign.GroupReservationId = "another-group";
        _sessions.Setup(r => r.GetByGuestSessionId("x")).ReturnsAsync(foreign);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.DeleteSessionAsync(OwnerId, GroupId, "x"));
        Assert.Contains("does not belong", ex.Message);
        _sessions.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task DeleteSessionAsync_SessionNotFound_Throws404()
    {
        _sessions.Setup(r => r.GetByGuestSessionId("missing")).ReturnsAsync((GuestSession?)null);
        await Assert.ThrowsAsync<GuestSessionNotFoundException>(() => _service.DeleteSessionAsync(OwnerId, GroupId, "missing"));
    }

    [Fact]
    public async Task DeleteSessionAsync_NotOwner_Throws403()
    {
        await Assert.ThrowsAsync<ActionForbiddenException>(() => _service.DeleteSessionAsync(OtherUserId, GroupId, "anna"));
        _sessions.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task DeleteSessionAsync_OwnerOwnSession_Throws400()
    {
        GuestSession owner = Owner("owner-session");
        _sessions.Setup(r => r.GetByGuestSessionId("owner-session")).ReturnsAsync(owner);

        await Assert.ThrowsAsync<ValidationException>(() => _service.DeleteSessionAsync(OwnerId, GroupId, "owner-session"));
        _sessions.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task DeleteSessionAsync_GroupSubmitted_Throws409()
    {
        _group.Status = GroupReservationStatus.Submitted;
        await Assert.ThrowsAsync<GroupReservationSubmittedException>(() => _service.DeleteSessionAsync(OwnerId, GroupId, "anna"));
    }

    // ------------------------------------------------------------------ US15 + US16: owner removes a guest's dish

    [Fact]
    public async Task RemoveGuestDishAsync_RemovesDish_AndLeavesNoticeForGuest()
    {
        GuestSession anna = Guest("anna", "Anna", submitted: true, (_salad.Id, 2), (_soup.Id, 1));
        _sessions.Setup(r => r.GetByGuestSessionId("anna")).ReturnsAsync(anna);
        _sessions.Setup(r => r.GetAllByGroupReservationIdAsync(GroupId)).ReturnsAsync(new List<GuestSession> { anna });

        GetAllSessionsResponse result = await _service.RemoveGuestDishAsync(OwnerId, GroupId, "anna", _soup.Id);

        Assert.DoesNotContain(anna.Dishes, d => d.DishId == _soup.Id);
        GuestNotice notice = Assert.Single(anna.Notices!);
        Assert.Equal(_soup.Id, notice.DishId);
        Assert.Contains("Tom Yum", notice.Message);
        _sessions.Verify(r => r.SaveAsync(anna), Times.Once);
        Assert.Equal(38m, result.Total);
    }

    [Fact]
    public async Task RemoveGuestDishAsync_OwnSession_NoNotice()
    {
        GuestSession owner = Owner("owner-session", (_salad.Id, 1));
        _sessions.Setup(r => r.GetByGuestSessionId("owner-session")).ReturnsAsync(owner);
        _sessions.Setup(r => r.GetAllByGroupReservationIdAsync(GroupId)).ReturnsAsync(new List<GuestSession> { owner });

        await _service.RemoveGuestDishAsync(OwnerId, GroupId, "owner-session", _salad.Id);

        Assert.Empty(owner.Dishes);
        Assert.True(owner.Notices is null || owner.Notices.Count == 0);
    }

    [Fact]
    public async Task RemoveGuestDishAsync_DishNotInSession_Throws404()
    {
        GuestSession anna = Guest("anna", "Anna", submitted: false, (_salad.Id, 1));
        _sessions.Setup(r => r.GetByGuestSessionId("anna")).ReturnsAsync(anna);

        await Assert.ThrowsAsync<DishNotFoundException>(() => _service.RemoveGuestDishAsync(OwnerId, GroupId, "anna", _soup.Id));
        _sessions.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task RemoveGuestDishAsync_NotOwner_Throws403()
    {
        await Assert.ThrowsAsync<ActionForbiddenException>(() => _service.RemoveGuestDishAsync(OtherUserId, GroupId, "anna", _soup.Id));
    }

    [Fact]
    public async Task RemoveGuestDishAsync_GroupSubmitted_Throws409()
    {
        _group.Status = GroupReservationStatus.Submitted;
        await Assert.ThrowsAsync<GroupReservationSubmittedException>(() => _service.RemoveGuestDishAsync(OwnerId, GroupId, "anna", _soup.Id));
    }

    // ------------------------------------------------------------------ helpers

    private static Dish MakeDish(string id, string name, decimal price, DishState state = DishState.Available) => new()
    {
        Id = id,
        Name = name,
        Description = name,
        Price = price,
        Weight = "300 g",
        Calories = "200",
        Proteins = "1",
        Fats = "1",
        Carbohydrates = "1",
        Vitamins = "A",
        State = state,
        LocationId = "location-1",
    };

    private static GuestSession Guest(string id, string name, bool submitted, params (string DishId, int Quantity)[] dishes) => new()
    {
        Id = id,
        GroupReservationId = GroupId,
        GuestName = name,
        IsSubmitted = submitted,
        Dishes = dishes.Select(d => new OrderItem { DishId = d.DishId, Quantity = d.Quantity, State = OrderItemState.Pending }).ToList(),
    };

    private static GuestSession Owner(string id, params (string DishId, int Quantity)[] dishes)
    {
        GuestSession session = Guest(id, "Owner", submitted: false, dishes);
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
            ReservationAddress = "Rustaveli 48, Table 1",
            ReservationTime = "Oct 24, 2026, 7:00 p.m. - 8:30 p.m.",
            Dishes = existing.ToList(),
            Status = OrderStatus.Created,
        };
        _orders.Setup(r => r.GetOrderByReservationIdAsync(ReservationId)).ReturnsAsync(order);
        return order;
    }
}
