using Amazon.CognitoIdentityProvider.Model;
using APIHandler.Constants;
using APIHandler.Constants.Dishes;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Mappers;
using APIHandler.Models.GuestSessions;
using APIHandler.Models.Orders;
using APIHandler.Repositories.Auth;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using APIHandler.Services.Notifications;

namespace APIHandler.Services.GuestSessions;

public class GuestSessionService(
    IGroupReservationRepository groupReservationRepository,
    IGuestSessionRepository guestSessionRepository,
    IDishRepository dishRepository,
    IReservationRepository reservationRepository,
    IUserRepository userRepository,
    ITableRepository tableRepository,
    IGroupPreOrderService? groupPreOrderService = null,
    TimeProvider? clock = null) : IGuestSessionService
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<bool> IsAuthorizedAsync(string guestSessionToken, string guestSessionId)
    {
        // A malformed token is just an invalid token (401), not a server error.
        if (string.IsNullOrWhiteSpace(guestSessionToken))
            return false;

        var tokenBytes = new byte[guestSessionToken.Length];
        if (!Convert.TryFromBase64String(guestSessionToken, tokenBytes, out int bytesWritten))
            return false;

        string hash = Convert.ToBase64String(SHA256.HashData(tokenBytes.AsSpan(0, bytesWritten)));
        GuestSession? guestSession = await guestSessionRepository.GetByTokenHash(hash);
        if (guestSession is null)
            return false;

        return guestSession.Id == guestSessionId;
    }

    public async Task<CreatGuestSessionResponse> CreateGuestSessionAsync(CreateGuestSessionRequest request)
    {
        GroupReservation group = await GetGroupReservationAndValidateAsync(request.GroupReservationId);

        Task<Reservation?> reservationTask = reservationRepository.GetReservationByIdAsync(group.ReservationId);
        Task<int> countTask = guestSessionRepository.CountByGroupReservationId(group.GroupReservationId);

        Reservation reservation = await reservationTask ?? throw new GroupReservationExpiredException($"Associated reservation for group '{request.GroupReservationId}' not found.");

        if (reservation.Status != ReservationStatus.Reserved)
        {
            throw new GuestSessionExpiredException($"This pre-order session has expired or is no longer active. Current reservation status: '{reservation.Status}'.");
        }

        int joinedGuestsCount = await countTask;

        if (joinedGuestsCount + 1 >= reservation.GuestCount)
            throw new GuestLimitExceededException($"Guest limit exceeded. Maximum capacity is {reservation.GuestCount}, but {joinedGuestsCount} guests have already joined.");

        byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);

        var session = new GuestSession()
        {
            Id = Guid.NewGuid().ToString(),
            GuestName = request.GuestName,
            GroupReservationId = group.GroupReservationId,
            SessionTokenHash = Convert.ToBase64String(SHA256.HashData(tokenBytes)),
            Dishes = [],
        };

        await guestSessionRepository.SaveAsync(session);

        return new CreatGuestSessionResponse(session.Id, Convert.ToBase64String(tokenBytes));
    }

    public async Task<GuestMenuResponse> GetGuestMenuAsync(string guestSessionId)
    {
        var session = await guestSessionRepository.GetByGuestSessionId(guestSessionId)
            ?? throw new ValidationException($"Guest session '{guestSessionId}' not found.");

        await ValidateGroupReservationId(session.GroupReservationId);

        string locationId = await GetLocationIdForGroupAsync(session.GroupReservationId);
        var dishes = await dishRepository.GetDishesByLocationIdAsync(locationId);

        var dishesMapped = dishes
            .GroupBy(d => d.DishType)
            .ToDictionary(k => k.Key, GuestMenuDishMapper.Map);

        return new GuestMenuResponse(dishesMapped);
    }

    public async Task AddDishAsync(string guestSessionId, string dishId, int quantity)
    {
        if (quantity <= 0)
            throw new ValidationException($"Quantity ({quantity}) must be greater than zero.");

        GuestSession session = await GetGuestSessionAsync(guestSessionId);

        string locationId = await GetLocationIdForGroupAsync(session.GroupReservationId);
        var locationDishes = await dishRepository.GetDishesByLocationIdAsync(locationId);

        var dish = locationDishes.FirstOrDefault(d => d.Id == dishId)
            ?? throw new ValidationException($"Dish '{dishId}' does not exist or is not available at this location.");

        if (dish.State != DishState.Available)
            throw new DishIsNotAvailableException($"Dish '{dishId}' is currently unavailable. State: '{dish.State}'.");

        session.Dishes ??= [];

        var existingDish = session.Dishes.FirstOrDefault(d => d.DishId == dishId);

        if (existingDish != null)
        {
            existingDish.Quantity += quantity;
        }
        else
        {
            session.Dishes.Add(new OrderItem
            {
                DishId = dishId,
                Quantity = quantity,
                State = OrderItemState.Pending
            });
        }

        await guestSessionRepository.SaveAsync(session);
    }

    public async Task RemoveDishAsync(string guestSessionId, string dishId, int quantity)
    {
        if (quantity <= 0)
            throw new ValidationException("Quantity must be greater than zero.");

        GuestSession session = await GetGuestSessionAsync(guestSessionId);

        if (session.Dishes == null || session.Dishes.Count == 0)
            throw new DishNotFoundException($"Dish {dishId} not found in guest session.");

        var existingDish = session.Dishes.FirstOrDefault(d => d.DishId == dishId)
            ?? throw new DishNotFoundException($"Dish {dishId} not found in guest session.");

        existingDish.Quantity -= quantity;

        if (existingDish.Quantity <= 0)
        {
            session.Dishes.Remove(existingDish);
        }

        await guestSessionRepository.SaveAsync(session);
    }

    public async Task<GroupReservationStatusResponse> GetGroupReservationStatusAsync(string groupReservationId, string? guestSessionId = null)
    {
        await EnforceCutoffAsync(groupReservationId);

        var group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        var reservation = await reservationRepository.GetReservationByIdAsync(group.ReservationId)
            ?? throw new InvalidOperationException("Associated reservation not found.");

        int currentParticipants = await GetCurrentParticipantCountAsync(groupReservationId);
        DateTime now = _clock.GetUtcNow().UtcDateTime;
        bool hasStartTime = reservation.StartTime != default;

        return new GroupReservationStatusResponse
        {
            CurrentParticipants = currentParticipants,
            MaxCapacity = reservation.GuestCount,
            Status = group.Status,
            Removed = guestSessionId is not null && (group.RemovedSessionIds?.Contains(guestSessionId) ?? false),
            CutoffAt = hasStartTime ? GroupPreOrderRules.CutoffAt(reservation) : null,
            SecondsUntilCutoff = hasStartTime ? GroupPreOrderRules.SecondsUntilCutoff(reservation, now) : 0,
            IsLocked = group.Status != GroupReservationStatus.Open
                || (hasStartTime && GroupPreOrderRules.IsCutoffPassed(reservation, now)),
        };
    }

    // ---------------------------------------------------------------- US18/US19: notes, serving time, allergies

    public async Task UpdateDishDetailsAsync(string guestSessionId, string dishId, string? note, string? serveTime)
    {
        string? normalizedNote = GroupPreOrderService.NormalizeText(note, GroupPreOrderRules.NoteMaxLength, "Note");
        string? normalizedServeTime = GroupPreOrderService.NormalizeServeTime(serveTime);

        // Notes and serving times do not change totals, so they can be edited after the guest submitted the cart.
        GuestSession session = await GetSessionInOpenGroupAsync(guestSessionId);

        OrderItem item = (session.Dishes ?? []).FirstOrDefault(d => d.DishId == dishId)
            ?? throw new DishNotFoundException($"Dish {dishId} not found in guest session.");

        GroupPreOrderService.ApplyDetails(item, normalizedNote, normalizedServeTime);
        await guestSessionRepository.SaveAsync(session);
    }

    public async Task UpdateAllergiesAsync(string guestSessionId, string? allergies)
    {
        string? normalized = GroupPreOrderService.NormalizeText(allergies ?? string.Empty, GroupPreOrderRules.AllergiesMaxLength, "Allergies");

        GuestSession session = await GetSessionInOpenGroupAsync(guestSessionId);
        session.Allergies = string.IsNullOrEmpty(normalized) ? null : normalized;
        await guestSessionRepository.SaveAsync(session);
    }

    /// <summary>US16 AC3 "Pick another dish": the guest edits a submitted cart again (before the cut-off).</summary>
    public async Task ReopenCartAsync(string guestSessionId)
    {
        GuestSession session = await GetSessionInOpenGroupAsync(guestSessionId);
        if (!session.IsSubmitted)
            return;

        session.IsSubmitted = false;
        await guestSessionRepository.SaveAsync(session);
    }

    /// <summary>US17: simulated payment of the guest's share.</summary>
    public async Task<GuestPaymentShare> PayAsync(string guestSessionId)
    {
        var session = await guestSessionRepository.GetByGuestSessionId(guestSessionId)
            ?? throw new GuestSessionNotFoundException("Guest session was not found.");

        if (groupPreOrderService is null)
            throw new ValidationException("Payments are not available.");

        return await groupPreOrderService.PayGuestShareAsync(session.GroupReservationId, guestSessionId);
    }

    
    /// <summary>US16: the guest has seen the owner's changes — clear the notices.</summary>
    public async Task AcknowledgeNoticesAsync(string guestSessionId)
    {
        var session = await guestSessionRepository.GetByGuestSessionId(guestSessionId)
            ?? throw new GuestSessionNotFoundException("Guest session was not found.");

        if (session.Notices is null || session.Notices.Count == 0)
            return;

        session.Notices = [];
        await guestSessionRepository.SaveAsync(session);
    }

    public async Task SubmitCartAsync(string guestSessionId)
    {
        GuestSession guestSession = await GetGuestSessionAsync(guestSessionId);

        if (!guestSession.Dishes.Any())
            throw new CartIsEmptyException();

        await ValidateDishesAsync(guestSession.Dishes);

        guestSession.IsSubmitted = true;
        await guestSessionRepository.SaveAsync(guestSession);
    }

    private async Task EnforceCutoffAsync(string groupReservationId)
    {
        // US13: after the cut-off the group is sent automatically and locked for everyone.
        if (groupPreOrderService is not null && !string.IsNullOrWhiteSpace(groupReservationId))
            await groupPreOrderService.EnforceCutoffAsync(groupReservationId);
    }

    private async Task<GuestSession> GetSessionInOpenGroupAsync(string sessionId)
    {
        var session = await guestSessionRepository.GetByGuestSessionId(sessionId)
            ?? throw new GuestSessionNotFoundException($"Guest session {sessionId} not found.");

        await ValidateGroupReservationId(session.GroupReservationId);
        return session;
    }

    private async Task<GroupReservation> GetGroupReservationAndValidateAsync(string groupReservationId)
    {
        await EnforceCutoffAsync(groupReservationId);

        GroupReservation? group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId);
        ValidateGroupReservation(group);
        return group;
    }

    private async Task<int> GetCurrentParticipantCountAsync(string groupReservationId)
    {
        var sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(groupReservationId);

        // Owner is always 1 active participant; the owner's own session (if created) is not a guest.
        // If sessions is null/empty, it safely evaluates to 0 joined guests + 1 owner = 1.
        return (sessions?.Count(s => !s.IsOwnerSession) ?? 0) + 1;
    }

    private async Task ValidateGroupReservationId(string groupReservationId) => await GetGroupReservationAndValidateAsync(groupReservationId);

    private static void ValidateGroupReservation([NotNull] GroupReservation? group)
    {
        if (group is null)
            throw new GroupReservationNotFoundException();

        if (group.Status == GroupReservationStatus.Closed)
            throw new GroupReservationExpiredException();

        if (group.Status == GroupReservationStatus.Submitted)
            throw new GroupReservationSubmittedException();
    }

    private async Task<GuestSession> GetGuestSessionAsync(string sessionId)
    {
        var session = await guestSessionRepository.GetByGuestSessionId(sessionId)
            ?? throw new GuestSessionNotFoundException($"Guest session {sessionId} not found.");

        if (session.IsSubmitted)
            throw new GuestSessionSubmittedException();

        await ValidateGroupReservationId(session.GroupReservationId);

        return session;
    }

    private async Task ValidateDishesAsync(IEnumerable<OrderItem> orders)
    {
        IEnumerable<string> dishIds = orders.Select(o => o.DishId).ToArray();
        IEnumerable<Dish> dishes = await dishRepository.GetDishesByIdsAsync(dishIds);

        if (dishes.FirstOrDefault(d => d.State != DishState.Available) is Dish dish)
            throw new DishIsNotAvailableException($"The dish '{dish.Name}' ({dish.Id}) is not available.");
    }

    private async Task<string> GetLocationIdForGroupAsync(string groupReservationId)
    {
        var group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        var reservation = await reservationRepository.GetReservationByIdAsync(group.ReservationId)
            ?? throw new ReservationNotFoundException("Parent reservation not found.");

        var table = await tableRepository.GetTableAsync(reservation.TableId)
            ?? throw new ValidationException("Associated table not found.");

        return table.LocationId;
    }

    
    private async Task ValidateReservationOwnerAsync(string groupReservationId, string userId)
    {
        var group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        var reservation = await reservationRepository.GetReservationByIdAsync(group.ReservationId)
            ?? throw new ReservationNotFoundException("Parent reservation not found.");

        if (reservation.CustomerId != userId)
        {
            throw new ActionForbiddenException("Only the reservation owner can perform this action.");
        }
    }

    public async Task CreateGroupOwnerSessionAsync(string userId, string groupReservationId)
    {
        var session = new GuestSession()
        {
            Id = Guid.NewGuid().ToString(),
            GroupReservationId = groupReservationId,
            GuestName = "Owner",
            Dishes = [],
            IsOwnerSession = true,
            OwnerUserId = userId,
        };

        await guestSessionRepository.SaveAsync(session);
    }

    public async Task<GetGuestSessionResponse> GetGuestSessionInfoAsync(string guestSessionId)
    {
        var session = await guestSessionRepository.GetByGuestSessionId(guestSessionId)
            ?? throw new GuestSessionNotFoundException("Guest session was not found.");

        await EnforceCutoffAsync(session.GroupReservationId);

        // Re-read: an automatic submit may have changed the session (owner snapshot) or the group.
        session = await guestSessionRepository.GetByGuestSessionId(guestSessionId) ?? session;
        session.Dishes ??= [];

        var dishIds = session.Dishes
            .Select(s => s.DishId)
            .Distinct()
            .ToArray();

        IEnumerable<Dish> allDishes = dishIds.Length == 0 ? Array.Empty<Dish>() : await dishRepository.GetDishesByIdsAsync(dishIds);
        var dishesById = allDishes
            .GroupBy(d => d.Id)
            .ToDictionary(g => g.Key, g => g.First());

        // Dishes deleted from the menu are skipped instead of failing the whole cart.
        var items = session.Dishes
            .Where(d => dishesById.ContainsKey(d.DishId))
            .Select(d => new OrderItemResponse
            {
                Dish = DishMapper.ToDishResponse(dishesById[d.DishId]),
                Quantity = d.Quantity,
                Note = d.Note,
                ServeTime = d.ServeTime ?? GroupPreOrderRules.DefaultServeTime(dishesById[d.DishId].DishType),
            })
            .ToArray();

        decimal total = items.Sum(i => i.Quantity * i.Dish.Price);

        GroupReservation? group = await groupReservationRepository.GetGroupReservationByIdAsync(session.GroupReservationId);
        Reservation? reservation = group is null ? null : await reservationRepository.GetReservationByIdAsync(group.ReservationId);

        string? ownerName = await GetUserFirstNameAsync(reservation?.CustomerId);
        string name = session.IsOwnerSession ? ownerName ?? session.GuestName : session.GuestName;

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        bool hasStartTime = reservation is not null && reservation.StartTime != default;

        GuestPaymentShare? payment = group is not null && groupPreOrderService is not null
            ? await groupPreOrderService.GetGuestPaymentShareAsync(group.GroupReservationId, session.Id)
            : null;

        return new GetGuestSessionResponse(name, items, total, session.IsSubmitted)
        {
            Notices = (session.Notices ?? [])
                .Select(n => new GuestNoticeResponse(n.Id, n.Message, n.DishId, n.CreatedAt)
                {
                    Type = n.Type ?? (n.DishId is null ? null : GuestNoticeTypes.DishRemoved),
                    ActorName = n.ActorName,
                    DishName = n.DishName,
                    Quantity = n.Quantity,
                    OldTotal = n.OldTotal,
                    NewTotal = n.NewTotal,
                    Amount = n.Amount,
                })
                .ToArray(),
            Allergies = session.Allergies,
            GroupStatus = group?.Status.ToString(),
            CutoffAt = hasStartTime ? GroupPreOrderRules.CutoffAt(reservation!) : null,
            SecondsUntilCutoff = hasStartTime ? GroupPreOrderRules.SecondsUntilCutoff(reservation!, now) : 0,
            IsLocked = group is null
                || group.Status != GroupReservationStatus.Open
                || (hasStartTime && GroupPreOrderRules.IsCutoffPassed(reservation!, now)),
            OwnerName = ownerName,
            Payment = payment,
            Rebook = group is null ? null : await GetRebookInfoAsync(group),
        };
    }

    /// <summary>US21 AC3: the organizer booked again — guests of the previous group get the new invite.</summary>
    private async Task<RebookInfo?> GetRebookInfoAsync(GroupReservation group)
    {
        if (string.IsNullOrWhiteSpace(group.NextReservationId))
            return null;

        GroupReservation? next = await groupReservationRepository.GetByReservationIdAsync(group.NextReservationId);
        if (next is null || next.Status != GroupReservationStatus.Open)
            return null;

        Reservation? nextReservation = await reservationRepository.GetReservationByIdAsync(next.ReservationId);
        if (nextReservation is null || nextReservation.Status != ReservationStatus.Reserved)
            return null;

        return new RebookInfo(next.ReservationToken, nextReservation.StartTime, nextReservation.EndTime);
    }

    private async Task<string?> GetUserFirstNameAsync(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        try
        {
            var user = await userRepository.GetUserByIdAsync(userId);
            return user?.Attributes?.FirstOrDefault(a => a.Name == "given_name")?.Value;
        }
        catch (Exception)
        {
            return null;
        }
    }

}




