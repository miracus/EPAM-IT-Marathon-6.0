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
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Services.GuestSessions;

public class GroupPreOrderService(
    IGroupReservationRepository groupReservationRepository,
    IGuestSessionRepository guestSessionRepository,
    IDishRepository dishRepository,
    IReservationRepository reservationRepository,
    IOrderRepository orderRepository,
    IUserRepository userRepository) : IGroupPreOrderService
{
    internal const string OwnerFallbackName = "Owner";

    // ---------------------------------------------------------------- L1: GET /guest/groups/{id}/sessions

    public async Task<GetAllSessionsResponse> GetSessionsAsync(string userId, string groupReservationId)
    {
        (GroupReservation group, Reservation reservation) = await LoadGroupForOwnerAsync(userId, groupReservationId);
        List<GuestSession> sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId);
        return await BuildSnapshotAsync(sessions, group, reservation);
    }

    // ---------------------------------------------------------------- L2: GET /reservations/{id}/guest-session-id

    public async Task<OwnerSessionIdResponse> GetOwnerSessionIdAsync(string userId, string reservationId)
    {
        Reservation reservation = await reservationRepository.GetReservationByIdAsync(reservationId)
            ?? throw new ReservationNotFoundException($"Reservation {reservationId} was not found.");

        EnsureOwner(reservation, userId);

        GroupReservation group = await groupReservationRepository.GetByReservationIdAsync(reservationId)
            ?? throw new GroupReservationNotFoundException();

        List<GuestSession> sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId);

        GuestSession? ownerSession = sessions.FirstOrDefault(s => s.IsOwnerSession && s.OwnerUserId == userId)
            ?? sessions.FirstOrDefault(s => s.IsOwnerSession);

        if (ownerSession is null)
        {
            ownerSession = new GuestSession
            {
                Id = Guid.NewGuid().ToString(),
                GroupReservationId = group.GroupReservationId,
                GuestName = OwnerFallbackName,
                Dishes = [],
                IsOwnerSession = true,
                OwnerUserId = userId,
            };

            await guestSessionRepository.SaveAsync(ownerSession);
        }

        return new OwnerSessionIdResponse(ownerSession.Id);
    }

    // ---------------------------------------------------------------- L2: POST /group-reservations/{id}/submit

    public async Task<IReadOnlyList<SubmittedDishResponse>> SubmitAsync(string userId, string groupReservationId, string reservationId)
    {
        GroupReservation group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        if (!string.Equals(group.ReservationId, reservationId, StringComparison.Ordinal))
            throw new ValidationException("Reservation does not belong to this group reservation.");

        Reservation reservation = await reservationRepository.GetReservationByIdAsync(reservationId)
            ?? throw new ReservationNotFoundException($"Reservation {reservationId} was not found.");

        EnsureOwner(reservation, userId);
        EnsureGroupIsOpen(group);

        List<GuestSession> sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId);
        List<GuestSession> submittedSessions = sessions.Where(s => s.IsSubmitted).ToList();

        if (submittedSessions.Count == 0)
            throw new ValidationException("There are no submitted guest sessions in this group reservation.");

        List<OrderItem> submittedItems = submittedSessions
            .SelectMany(s => s.Dishes ?? [])
            .Where(d => d.Quantity > 0)
            .ToList();

        if (submittedItems.Count == 0)
            throw new ValidationException("Submitted guest sessions do not contain any dishes.");

        // Dishes that went On Stop / Out of stock after guests chose them are not sent to the restaurant (US12 AC6, US13 AC5).
        string[] dishIds = submittedItems.Select(i => i.DishId).Distinct().ToArray();
        HashSet<string> availableDishIds = (await dishRepository.GetDishesByIdsAsync(dishIds))
            .Where(d => d.State == DishState.Available)
            .Select(d => d.Id)
            .ToHashSet();

        List<SubmittedDishResponse> merged = submittedItems
            .Where(i => availableDishIds.Contains(i.DishId))
            .GroupBy(i => i.DishId)
            .Select(g => new SubmittedDishResponse(g.Key, g.Sum(i => i.Quantity)))
            .ToList();

        if (merged.Count == 0)
            throw new ValidationException("None of the dishes in submitted guest sessions are available.");

        Order order = await orderRepository.GetOrderByReservationIdAsync(reservationId)
            ?? throw new ValidationException($"Order for reservation {reservationId} was not found.");

        if (order.Status == OrderStatus.Cancelled)
            throw new ValidationException($"Order for reservation {reservationId} was cancelled.");

        order.Dishes ??= [];
        foreach (SubmittedDishResponse line in merged)
        {
            OrderItem? existing = order.Dishes.FirstOrDefault(d => d.DishId == line.DishId);
            if (existing is not null)
            {
                existing.Quantity += line.Quantity;
            }
            else
            {
                order.Dishes.Add(new OrderItem
                {
                    DishId = line.DishId,
                    Quantity = line.Quantity,
                    State = OrderItemState.Pending,
                });
            }
        }

        order.Status = OrderStatus.SubmittedByCustomer;
        await orderRepository.SaveOrderAsync(order);

        group.Status = GroupReservationStatus.Submitted;
        await groupReservationRepository.SaveGroupReservationAsync(group);

        return merged;
    }

    // ---------------------------------------------------------------- L3: DELETE /guest/groups/{id}/sessions/{sessionId}

    public async Task<GetAllSessionsResponse> DeleteSessionAsync(string userId, string groupReservationId, string guestSessionId)
    {
        (GroupReservation group, Reservation reservation) = await LoadGroupForOwnerAsync(userId, groupReservationId);
        EnsureGroupIsOpen(group);

        GuestSession session = await LoadSessionOfGroupAsync(guestSessionId, group.GroupReservationId);

        if (session.IsOwnerSession)
            throw new ValidationException("The organizer's own session cannot be removed from the group.");

        await guestSessionRepository.DeleteAsync(session);

        List<GuestSession> remaining = (await guestSessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId))
            .Where(s => s.Id != session.Id) // guard against eventually consistent GSI reads
            .ToList();

        return await BuildSnapshotAsync(remaining, group, reservation);
    }

    // ---------------------------------------------------------------- US15/US16: DELETE /guest/groups/{id}/sessions/{sessionId}/dishes/{dishId}

    public async Task<GetAllSessionsResponse> RemoveGuestDishAsync(string userId, string groupReservationId, string guestSessionId, string dishId)
    {
        (GroupReservation group, Reservation reservation) = await LoadGroupForOwnerAsync(userId, groupReservationId);
        EnsureGroupIsOpen(group);

        GuestSession session = await LoadSessionOfGroupAsync(guestSessionId, group.GroupReservationId);

        session.Dishes ??= [];
        OrderItem item = session.Dishes.FirstOrDefault(d => d.DishId == dishId)
            ?? throw new DishNotFoundException($"Dish {dishId} not found in guest session {guestSessionId}.");

        session.Dishes.Remove(item);

        if (!session.IsOwnerSession)
        {
            Dish? dish = await dishRepository.GetDishByIdAsync(dishId);
            string dishName = string.IsNullOrWhiteSpace(dish?.Name) ? "a dish" : dish!.Name;

            session.Notices ??= [];
            session.Notices.Add(new GuestNotice
            {
                DishId = dishId,
                Message = $"The organizer removed {dishName} from your order.",
            });
        }

        await guestSessionRepository.SaveAsync(session);

        List<GuestSession> sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId);

        // Use the saved copy for the edited session in case the index read is stale.
        List<GuestSession> snapshotSessions = sessions.Select(s => s.Id == session.Id ? session : s).ToList();
        if (snapshotSessions.All(s => s.Id != session.Id))
            snapshotSessions.Add(session);

        return await BuildSnapshotAsync(snapshotSessions, group, reservation);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<(GroupReservation Group, Reservation Reservation)> LoadGroupForOwnerAsync(string userId, string groupReservationId)
    {
        GroupReservation group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        Reservation reservation = await reservationRepository.GetReservationByIdAsync(group.ReservationId)
            ?? throw new ReservationNotFoundException("Parent reservation not found.");

        EnsureOwner(reservation, userId);
        return (group, reservation);
    }

    private async Task<GuestSession> LoadSessionOfGroupAsync(string guestSessionId, string groupReservationId)
    {
        GuestSession session = await guestSessionRepository.GetByGuestSessionId(guestSessionId)
            ?? throw new GuestSessionNotFoundException($"Guest session with id {guestSessionId} not found.");

        if (session.GroupReservationId != groupReservationId)
            throw new ValidationException("Guest session does not belong to this group reservation.");

        return session;
    }

    private static void EnsureOwner(Reservation reservation, string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)
            || !string.Equals(reservation.CustomerId, userId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ActionForbiddenException("Only the reservation owner can perform this action.");
        }
    }

    private static void EnsureGroupIsOpen(GroupReservation group)
    {
        if (group.Status == GroupReservationStatus.Submitted)
            throw new GroupReservationSubmittedException();

        if (group.Status == GroupReservationStatus.Closed)
            throw new GroupReservationExpiredException();
    }

    private async Task<GetAllSessionsResponse> BuildSnapshotAsync(
        IReadOnlyCollection<GuestSession> sessions, GroupReservation group, Reservation reservation)
    {
        // Owner first (US12 AC2), then guests in their stored order.
        List<GuestSession> ordered = sessions
            .OrderByDescending(s => s.IsOwnerSession)
            .ToList();

        string[] dishIds = ordered
            .SelectMany(s => s.Dishes ?? [])
            .Select(d => d.DishId)
            .Distinct()
            .ToArray();

        IEnumerable<Dish> dishes = dishIds.Length == 0
            ? Array.Empty<Dish>()
            : await dishRepository.GetDishesByIdsAsync(dishIds);

        Dictionary<string, Dish> dishesById = dishes
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.Id))
            .GroupBy(d => d.Id)
            .ToDictionary(g => g.Key, g => g.First());

        string? ownerName = ordered.Any(s => s.IsOwnerSession)
            ? await GetOwnerDisplayNameAsync(reservation.CustomerId)
            : null;

        var participants = new List<Participant>();
        foreach (GuestSession session in ordered)
        {
            // Dishes deleted from the menu are skipped instead of failing the whole snapshot.
            List<(OrderItem Item, Dish Dish)> lines = (session.Dishes ?? [])
                .Where(d => d.Quantity > 0 && dishesById.ContainsKey(d.DishId))
                .Select(d => (d, dishesById[d.DishId]))
                .ToList();

            OrderItemResponse[] items = lines
                .Select(l => new OrderItemResponse { Dish = DishMapper.ToDishResponse(l.Dish), Quantity = l.Item.Quantity })
                .ToArray();

            decimal total = lines
                .Where(l => l.Dish.State == DishState.Available)
                .Sum(l => l.Dish.Price * l.Item.Quantity);

            participants.Add(new Participant(
                session.Id,
                session.IsOwnerSession ? ParticipantType.Owner : ParticipantType.Guest,
                session.IsOwnerSession ? ownerName ?? session.GuestName : session.GuestName,
                items,
                total,
                session.IsSubmitted));
        }

        List<DishSummary> summary = ordered
            .SelectMany(s => s.Dishes ?? [])
            .Where(d => d.Quantity > 0 && dishesById.ContainsKey(d.DishId))
            .GroupBy(d => d.DishId)
            .Select(g =>
            {
                Dish dish = dishesById[g.Key];
                int quantity = g.Sum(d => d.Quantity);
                bool available = dish.State == DishState.Available;
                return new DishSummary(dish.Id, dish.Name, quantity, available ? dish.Price * quantity : 0m, available);
            })
            .OrderBy(s => s.IsAvailable ? 0 : 1)
            .ThenByDescending(s => s.Quantity)
            .ToList();

        return new GetAllSessionsResponse(participants, participants.Sum(p => p.Total))
        {
            SummaryByDish = summary,
            HasUnavailableDishes = summary.Any(s => !s.IsAvailable),
            GroupStatus = group.Status.ToString(),
        };
    }

    private async Task<string> GetOwnerDisplayNameAsync(string? customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            return OwnerFallbackName;

        try
        {
            var user = await userRepository.GetUserByIdAsync(customerId);
            string? givenName = user?.Attributes?.FirstOrDefault(a => a.Name == "given_name")?.Value;
            return string.IsNullOrWhiteSpace(givenName) ? OwnerFallbackName : givenName;
        }
        catch (Exception)
        {
            // The snapshot must not fail because Cognito is unavailable.
            return OwnerFallbackName;
        }
    }
}
