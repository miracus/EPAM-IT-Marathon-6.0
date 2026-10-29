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
using APIHandler.Repositories.Tables;
using APIHandler.Services.GuestSessions;
using Moq;

namespace APIHandler.Tests.Services;

/// <summary>Group pre-order extensions US13–US21 (guest side), wired with the real owner-side service.</summary>
public class GuestSessionExtensionsTests
{
    private const string GroupId = "group-1";
    private const string ReservationId = "reservation-1";
    private static readonly DateTime Start = new(2026, 11, 7, 19, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IGroupReservationRepository> _groups = new();
    private readonly Mock<IGuestSessionRepository> _sessions = new();
    private readonly Mock<IDishRepository> _dishes = new();
    private readonly Mock<IReservationRepository> _reservations = new();
    private readonly Mock<IOrderRepository> _orders = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITableRepository> _tables = new();
    private readonly FixedClock _clock = new(Start.AddHours(-5));
    private readonly GuestSessionService _service;

    private readonly GroupReservation _group;
    private readonly Reservation _reservation;
    private readonly GuestSession _anna;
    private readonly Dish _salad = new()
    {
        Id = "salad", Name = "Spring Salad", Description = "", Price = 19m, Weight = "", Calories = "", Proteins = "",
        Fats = "", Carbohydrates = "", Vitamins = "", State = DishState.Available, DishType = DishType.Appetizer,
    };

    public GuestSessionExtensionsTests()
    {
        var groupPreOrder = new GroupPreOrderService(
            _groups.Object, _sessions.Object, _dishes.Object, _reservations.Object, _orders.Object, _users.Object, _clock);
        _service = new GuestSessionService(
            _groups.Object, _sessions.Object, _dishes.Object, _reservations.Object, _users.Object, _tables.Object, groupPreOrder, _clock);

        _group = new GroupReservation { GroupReservationId = GroupId, ReservationId = ReservationId, ReservationToken = "token-1" };
        _reservation = new Reservation
        {
            Id = ReservationId, TableId = "table-1", WaiterId = "waiter-1", CustomerId = "owner", GuestCount = 4,
            Status = ReservationStatus.Reserved, StartTime = Start, EndTime = Start.AddMinutes(90),
        };
        _anna = new GuestSession
        {
            Id = "anna", GroupReservationId = GroupId, GuestName = "Anna",
            Dishes = [new OrderItem { DishId = _salad.Id, Quantity = 1, State = OrderItemState.Pending }],
        };

        _groups.Setup(r => r.GetGroupReservationByIdAsync(GroupId)).ReturnsAsync(() => _group);
        _groups.Setup(r => r.GetByReservationIdAsync(ReservationId)).ReturnsAsync(() => _group);
        _reservations.Setup(r => r.GetReservationByIdAsync(ReservationId)).ReturnsAsync(() => _reservation);
        _sessions.Setup(r => r.GetByGuestSessionId("anna")).ReturnsAsync(() => _anna);
        _sessions.Setup(r => r.GetAllByGroupReservationIdAsync(GroupId)).ReturnsAsync(() => [_anna]);
        _dishes.Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync([_salad]);
        _dishes.Setup(r => r.GetDishesByLocationIdAsync("loc-1")).ReturnsAsync([_salad]);
        _tables.Setup(r => r.GetTableAsync("table-1")).ReturnsAsync(new Table { Id = "table-1", LocationId = "loc-1", Capacity = 4, TableNumber = 1 });
    }

    [Fact]
    public async Task UpdateDishDetails_WorksAlsoAfterTheGuestSubmitted()
    {
        _anna.IsSubmitted = true;

        await _service.UpdateDishDetailsAsync("anna", _salad.Id, "no onion", ServeTimes.AfterMains);

        Assert.Equal("no onion", _anna.Dishes[0].Note);
        Assert.Equal(ServeTimes.AfterMains, _anna.Dishes[0].ServeTime);
    }

    [Fact]
    public async Task UpdateDishDetails_EmptyNoteClearsIt()
    {
        _anna.Dishes[0].Note = "old";

        await _service.UpdateDishDetailsAsync("anna", _salad.Id, "", null);

        Assert.Null(_anna.Dishes[0].Note);
    }

    [Fact]
    public async Task UpdateDishDetails_UnknownDish_IsNotFound()
    {
        await Assert.ThrowsAsync<DishNotFoundException>(() => _service.UpdateDishDetailsAsync("anna", "missing", "x", null));
    }

    [Fact]
    public async Task UpdateAllergies_SavesTrimmedValue()
    {
        await _service.UpdateAllergiesAsync("anna", "  Nuts  ");

        Assert.Equal("Nuts", _anna.Allergies);
    }

    [Fact]
    public async Task ReopenCart_LetsTheGuestEditAgain()
    {
        _anna.IsSubmitted = true;

        await _service.ReopenCartAsync("anna");

        Assert.False(_anna.IsSubmitted);
    }

    [Fact]
    public async Task AddDish_AfterCutoff_IsRejectedBecauseTheOrderWasSent()
    {
        _clock.Now = Start.AddHours(-1);
        _orders.Setup(r => r.GetOrderByReservationIdAsync(ReservationId)).ReturnsAsync((Order?)null);

        await Assert.ThrowsAsync<GroupReservationSubmittedException>(() => _service.AddDishAsync("anna", _salad.Id, 1));
        Assert.Equal(GroupReservationStatus.Submitted, _group.Status);
    }

    [Fact]
    public async Task Status_TellsARemovedGuestTheyWereRemoved()
    {
        _group.RemovedSessionIds = ["maks"];

        GroupReservationStatusResponse removed = await _service.GetGroupReservationStatusAsync(GroupId, "maks");
        GroupReservationStatusResponse stillHere = await _service.GetGroupReservationStatusAsync(GroupId, "anna");

        Assert.True(removed.Removed);
        Assert.False(stillHere.Removed);
        Assert.Equal(Start.AddHours(-2), removed.CutoffAt);
        Assert.False(removed.IsLocked);
    }

    [Fact]
    public async Task GuestSessionInfo_ReturnsCutoffNoticesAndServeTime()
    {
        _anna.Allergies = "Nuts";
        _anna.Notices =
        [
            new GuestNotice
            {
                Type = GuestNoticeTypes.DishRemoved, DishId = "soup", DishName = "Tom Yum", Quantity = 1,
                OldTotal = 41m, NewTotal = 19m, ActorName = "Oleh", Message = "Oleh removed Tom Yum from your order.",
            },
        ];

        GetGuestSessionResponse result = await _service.GetGuestSessionInfoAsync("anna");

        Assert.Equal("Nuts", result.Allergies);
        Assert.Equal(Start.AddHours(-2), result.CutoffAt);
        Assert.Equal(3 * 3600, result.SecondsUntilCutoff);
        Assert.False(result.IsLocked);
        Assert.Equal("Open", result.GroupStatus);
        Assert.Equal(ServeTimes.OnArrival, Assert.Single(result.Items).ServeTime);
        GuestNoticeResponse notice = Assert.Single(result.Notices);
        Assert.Equal(GuestNoticeTypes.DishRemoved, notice.Type);
        Assert.Equal(41m, notice.OldTotal);
        Assert.Null(result.Rebook);
        Assert.Null(result.Payment);
    }

    [Fact]
    public async Task GuestSessionInfo_AfterRebook_HasTheNewInvite()
    {
        _group.Status = GroupReservationStatus.Submitted;
        _group.NextReservationId = "reservation-2";
        _groups.Setup(r => r.GetByReservationIdAsync("reservation-2")).ReturnsAsync(new GroupReservation
        {
            GroupReservationId = "group-2", ReservationId = "reservation-2", ReservationToken = "token-2",
            Status = GroupReservationStatus.Open,
        });
        _reservations.Setup(r => r.GetReservationByIdAsync("reservation-2")).ReturnsAsync(new Reservation
        {
            Id = "reservation-2", TableId = "table-1", WaiterId = "waiter-1", CustomerId = "owner", GuestCount = 4,
            Status = ReservationStatus.Reserved, StartTime = Start.AddDays(7), EndTime = Start.AddDays(7).AddMinutes(90),
        });

        GetGuestSessionResponse result = await _service.GetGuestSessionInfoAsync("anna");

        Assert.True(result.IsLocked);
        Assert.Equal("token-2", result.Rebook!.InviteToken);
        Assert.Equal(Start.AddDays(7), result.Rebook.StartTime);
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public DateTime Now { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }
}
