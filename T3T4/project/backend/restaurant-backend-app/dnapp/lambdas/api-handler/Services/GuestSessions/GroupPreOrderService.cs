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
    IUserRepository userRepository,
    TimeProvider? clock = null) : IGroupPreOrderService
{
    internal const string OwnerFallbackName = "Owner";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

    // ---------------------------------------------------------------- L1: GET /guest/groups/{id}/sessions

    public async Task<GetAllSessionsResponse> GetSessionsAsync(string userId, string groupReservationId)
    {
        (GroupReservation group, Reservation reservation) = await LoadGroupForOwnerAsync(userId, groupReservationId);
        await EnforceCutoffCoreAsync(group, reservation);
        return await SnapshotAsync(group, reservation);
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
        GuestSession ownerSession = await GetOrCreateOwnerSessionAsync(sessions, group, userId);
        return new OwnerSessionIdResponse(ownerSession.Id);
    }

    // ---------------------------------------------------------------- L2 / US13: POST /group-reservations/{id}/submit

    public async Task<IReadOnlyList<SubmittedDishResponse>> SubmitAsync(string userId, string groupReservationId, string reservationId)
    {
        GroupReservation group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        if (!string.Equals(group.ReservationId, reservationId, StringComparison.Ordinal))
            throw new ValidationException("Reservation does not belong to this group reservation.");

        Reservation reservation = await reservationRepository.GetReservationByIdAsync(reservationId)
            ?? throw new ReservationNotFoundException($"Reservation {reservationId} was not found.");

        EnsureOwner(reservation, userId);

        // After the cut-off the pre-order has already been sent automatically: the manual submit gets 409.
        await EnforceCutoffCoreAsync(group, reservation);
        EnsureGroupIsOpen(group);

        List<GuestSession> sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId);
        return await SubmitCoreAsync(group, reservation, sessions, userId, automatic: false);
    }

    // ---------------------------------------------------------------- L3: DELETE /guest/groups/{id}/sessions/{sessionId}

    public async Task<GetAllSessionsResponse> DeleteSessionAsync(string userId, string groupReservationId, string guestSessionId)
    {
        (GroupReservation group, Reservation reservation) = await LoadOpenGroupForOwnerAsync(userId, groupReservationId);

        GuestSession session = await LoadSessionOfGroupAsync(guestSessionId, group.GroupReservationId);

        if (session.IsOwnerSession)
            throw new ValidationException("The organizer's own session cannot be removed from the group.");

        await guestSessionRepository.DeleteAsync(session);

        // US16 AC5: the removed guest's link says "You were removed from this group order".
        group.RemovedSessionIds ??= [];
        if (!group.RemovedSessionIds.Contains(session.Id))
            group.RemovedSessionIds.Add(session.Id);
        await groupReservationRepository.SaveGroupReservationAsync(group);

        List<GuestSession> remaining = (await guestSessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId))
            .Where(s => s.Id != session.Id) // guard against eventually consistent GSI reads
            .ToList();

        return await BuildSnapshotAsync(remaining, group, reservation);
    }

    // ---------------------------------------------------------------- US15/US16: DELETE .../sessions/{sessionId}/dishes/{dishId}

    public async Task<GetAllSessionsResponse> RemoveGuestDishAsync(string userId, string groupReservationId, string guestSessionId, string dishId)
    {
        (GroupReservation group, Reservation reservation) = await LoadOpenGroupForOwnerAsync(userId, groupReservationId);

        GuestSession session = await LoadSessionOfGroupAsync(guestSessionId, group.GroupReservationId);

        session.Dishes ??= [];
        OrderItem item = session.Dishes.FirstOrDefault(d => d.DishId == dishId)
            ?? throw new DishNotFoundException($"Dish {dishId} not found in guest session {guestSessionId}.");

        if (!session.IsOwnerSession)
        {
            Dictionary<string, Dish> dishes = await LoadDishesAsync(session.Dishes.Select(d => d.DishId));
            decimal oldTotal = TotalOf(session.Dishes, dishes);

            session.Dishes.Remove(item);

            decimal newTotal = TotalOf(session.Dishes, dishes);
            string dishName = dishes.TryGetValue(dishId, out Dish? dish) && !string.IsNullOrWhiteSpace(dish.Name) ? dish.Name : "a dish";
            string ownerName = await GetOwnerDisplayNameAsync(reservation.CustomerId);

            AddNotice(session, new GuestNotice
            {
                Type = GuestNoticeTypes.DishRemoved,
                DishId = dishId,
                DishName = dishName,
                Quantity = item.Quantity,
                OldTotal = oldTotal,
                NewTotal = newTotal,
                ActorName = ownerName,
                CreatedAt = UtcNow,
                Message = $"{ownerName} removed {dishName} from your order.",
            });
            session.ChangedByOwner = true;
        }
        else
        {
            session.Dishes.Remove(item);
        }

        await guestSessionRepository.SaveAsync(session);
        return await SnapshotWithAsync(session, group, reservation);
    }

    // ---------------------------------------------------------------- US15 AC2: Undo

    public async Task<GetAllSessionsResponse> RestoreGuestDishAsync(
        string userId, string groupReservationId, string guestSessionId, string dishId, RestoreDishRequest request)
    {
        if (request.Quantity <= 0)
            throw new ValidationException("Quantity must be greater than zero.");

        string? note = NormalizeText(request.Note, GroupPreOrderRules.NoteMaxLength, "Note");
        string? serveTime = NormalizeServeTime(request.ServeTime);

        (GroupReservation group, Reservation reservation) = await LoadOpenGroupForOwnerAsync(userId, groupReservationId);

        GuestSession session = await LoadSessionOfGroupAsync(guestSessionId, group.GroupReservationId);
        if (session.IsOwnerSession)
            throw new ValidationException("Use your own pre-order to change your dishes.");

        session.Dishes ??= [];
        OrderItem? existing = session.Dishes.FirstOrDefault(d => d.DishId == dishId);
        if (existing is not null)
        {
            existing.Quantity += request.Quantity;
        }
        else
        {
            session.Dishes.Add(new OrderItem
            {
                DishId = dishId,
                Quantity = request.Quantity,
                State = OrderItemState.Pending,
                Note = string.IsNullOrEmpty(note) ? null : note,
                ServeTime = serveTime,
            });
        }

        // The guest no longer needs to see "removed" for a dish that came back.
        session.Notices = (session.Notices ?? [])
            .Where(n => !(IsRemovalNotice(n) && n.DishId == dishId))
            .ToList();
        session.ChangedByOwner = session.Notices.Any(IsRemovalNotice);

        await guestSessionRepository.SaveAsync(session);
        return await SnapshotWithAsync(session, group, reservation);
    }

    // ---------------------------------------------------------------- US15 AC4: Ask to change order

    public async Task<GetAllSessionsResponse> RequestChangeAsync(string userId, string groupReservationId, string guestSessionId)
    {
        (GroupReservation group, Reservation reservation) = await LoadOpenGroupForOwnerAsync(userId, groupReservationId);

        GuestSession session = await LoadSessionOfGroupAsync(guestSessionId, group.GroupReservationId);
        if (session.IsOwnerSession)
            throw new ValidationException("The organizer cannot ask themselves to change the order.");

        string ownerName = await GetOwnerDisplayNameAsync(reservation.CustomerId);

        // The guest can edit the cart again and submit it once more.
        session.IsSubmitted = false;
        AddNotice(session, new GuestNotice
        {
            Type = GuestNoticeTypes.ChangeRequested,
            ActorName = ownerName,
            CreatedAt = UtcNow,
            Message = $"{ownerName} asks you to change your order.",
        });

        await guestSessionRepository.SaveAsync(session);
        return await SnapshotWithAsync(session, group, reservation);
    }

    // ---------------------------------------------------------------- US14: reminders

    public async Task<RemindersResponse> SendRemindersAsync(string userId, string groupReservationId, RemindRequest request)
    {
        (GroupReservation group, Reservation reservation) = await LoadGroupForOwnerAsync(userId, groupReservationId);
        await EnforceCutoffCoreAsync(group, reservation);

        HashSet<string>? requestedIds = request.GuestSessionIds is { Count: > 0 }
            ? request.GuestSessionIds.ToHashSet(StringComparer.Ordinal)
            : null;

        List<GuestSession> sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId);
        string ownerName = await GetOwnerDisplayNameAsync(reservation.CustomerId);
        DateTime now = UtcNow;

        List<(GuestSession Session, GuestNotice Notice)> targets;

        if (group.Status == GroupReservationStatus.Open)
        {
            // AC1/AC2: guests who have not chosen any dishes yet (or the ones the owner picked).
            targets = sessions
                .Where(s => !s.IsOwnerSession)
                .Where(s => requestedIds is null ? (s.Dishes ?? []).Count == 0 : requestedIds.Contains(s.Id))
                .Select(s => (s, new GuestNotice
                {
                    Type = GuestNoticeTypes.Reminder,
                    ActorName = ownerName,
                    CreatedAt = now,
                    Message = $"{ownerName} reminds you to choose your dishes.",
                }))
                .ToList();
        }
        else if (group.Status == GroupReservationStatus.Submitted && group.PaymentMode is not null)
        {
            // AC5: at the payment step the reminder goes to those who haven't paid.
            PaymentView payment = await ComputePaymentAsync(group, sessions);
            HashSet<string> pending = payment.Participants
                .Where(p => p.Type == ParticipantType.Guest && !p.IsPaid)
                .Select(p => p.Id)
                .ToHashSet(StringComparer.Ordinal);

            targets = sessions
                .Where(s => pending.Contains(s.Id) && (requestedIds is null || requestedIds.Contains(s.Id)))
                .Select(s =>
                {
                    decimal amount = payment.Participants.First(p => p.Id == s.Id).Amount;
                    return (s, new GuestNotice
                    {
                        Type = GuestNoticeTypes.PaymentRequested,
                        ActorName = ownerName,
                        Amount = amount,
                        CreatedAt = now,
                        Message = $"{ownerName} reminds you to pay your share: ${amount:0.00}.",
                    });
                })
                .ToList();
        }
        else
        {
            EnsureGroupIsOpen(group);
            targets = [];
        }

        var reminded = new List<ReminderResult>();
        var skipped = new List<ReminderResult>();

        foreach ((GuestSession session, GuestNotice notice) in targets)
        {
            // AC4: at most one manual reminder per guest within 15 minutes.
            if (session.LastRemindedAt is DateTime last
                && now - GroupPreOrderRules.AsUtc(last) < TimeSpan.FromMinutes(GroupPreOrderRules.ReminderCooldownMinutes))
            {
                skipped.Add(new ReminderResult(session.Id, session.GuestName, last));
                continue;
            }

            AddNotice(session, notice);
            session.LastRemindedAt = now;
            await guestSessionRepository.SaveAsync(session);
            reminded.Add(new ReminderResult(session.Id, session.GuestName, now));
        }

        return new RemindersResponse(reminded, skipped);
    }

    // ---------------------------------------------------------------- US14 AC3: settings

    public async Task<GetAllSessionsResponse> UpdateSettingsAsync(string userId, string groupReservationId, GroupSettingsRequest request)
    {
        (GroupReservation group, Reservation reservation) = await LoadGroupForOwnerAsync(userId, groupReservationId);

        if (request.AutoRemind is bool autoRemind)
        {
            group.AutoRemindDisabled = !autoRemind;
            await groupReservationRepository.SaveGroupReservationAsync(group);
        }

        await EnforceCutoffCoreAsync(group, reservation);
        return await SnapshotAsync(group, reservation);
    }

    // ---------------------------------------------------------------- US18/US19: the owner's own dishes and allergies

    public async Task<GetAllSessionsResponse> UpdateOwnerDishAsync(string userId, string groupReservationId, string dishId, DishDetailsRequest request)
    {
        string? note = NormalizeText(request.Note, GroupPreOrderRules.NoteMaxLength, "Note");
        string? serveTime = NormalizeServeTime(request.ServeTime);

        (GroupReservation group, Reservation reservation) = await LoadOpenGroupForOwnerAsync(userId, groupReservationId);

        Order order = await orderRepository.GetOrderByReservationIdAsync(reservation.Id)
            ?? throw new DishNotFoundException($"Dish {dishId} is not in your pre-order.");

        OrderItem item = (order.Dishes ?? []).FirstOrDefault(d => d.DishId == dishId && d.State != OrderItemState.Canceled)
            ?? throw new DishNotFoundException($"Dish {dishId} is not in your pre-order.");

        ApplyDetails(item, note, serveTime);
        await orderRepository.SaveOrderAsync(order);

        return await SnapshotAsync(group, reservation);
    }

    public async Task<GetAllSessionsResponse> UpdateOwnerAllergiesAsync(string userId, string groupReservationId, AllergiesRequest request)
    {
        string? allergies = NormalizeText(request.Allergies ?? string.Empty, GroupPreOrderRules.AllergiesMaxLength, "Allergies");

        (GroupReservation group, Reservation reservation) = await LoadOpenGroupForOwnerAsync(userId, groupReservationId);

        List<GuestSession> sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId);
        GuestSession ownerSession = await GetOrCreateOwnerSessionAsync(sessions, group, userId);
        ownerSession.Allergies = string.IsNullOrEmpty(allergies) ? null : allergies;
        await guestSessionRepository.SaveAsync(ownerSession);

        return await SnapshotWithAsync(ownerSession, group, reservation);
    }

    // ---------------------------------------------------------------- US17: split the bill (simulated payments)

    public async Task<PaymentView> GetPaymentAsync(string userId, string groupReservationId)
    {
        (GroupReservation group, Reservation reservation) = await LoadGroupForOwnerAsync(userId, groupReservationId);
        await EnforceCutoffCoreAsync(group, reservation);
        EnsureSubmittedForPayment(group);

        List<GuestSession> sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId);
        return await ComputePaymentAsync(group, sessions);
    }

    public async Task<PaymentView> SetPaymentModeAsync(string userId, string groupReservationId, string? mode)
    {
        if (!PaymentModes.IsValid(mode))
            throw new ValidationException($"Payment mode must be one of: {PaymentModes.EachPaysOwn}, {PaymentModes.OwnerPaysAll}, {PaymentModes.SplitEqually}.");

        (GroupReservation group, Reservation reservation) = await LoadGroupForOwnerAsync(userId, groupReservationId);
        await EnforceCutoffCoreAsync(group, reservation);
        EnsureSubmittedForPayment(group);

        group.PaymentMode = mode;
        await groupReservationRepository.SaveGroupReservationAsync(group);

        List<GuestSession> sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId);
        return await ComputePaymentAsync(group, sessions);
    }

    public async Task<PaymentView> SendPaymentLinksAsync(string userId, string groupReservationId)
    {
        (GroupReservation group, Reservation reservation) = await LoadGroupForOwnerAsync(userId, groupReservationId);
        EnsureSubmittedForPayment(group);

        if (group.PaymentMode is null)
            throw new ValidationException("Choose how the group pays first.");

        List<GuestSession> sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId);
        PaymentView payment = await ComputePaymentAsync(group, sessions);
        string ownerName = await GetOwnerDisplayNameAsync(reservation.CustomerId);
        DateTime now = UtcNow;

        foreach (PaymentParticipant participant in payment.Participants.Where(p => p.Type == ParticipantType.Guest && !p.IsPaid))
        {
            GuestSession session = sessions.First(s => s.Id == participant.Id);
            session.PaymentLinkSentAt = now;
            AddNotice(session, new GuestNotice
            {
                Type = GuestNoticeTypes.PaymentRequested,
                ActorName = ownerName,
                Amount = participant.Amount,
                CreatedAt = now,
                Message = $"{ownerName} asks you to pay your share: ${participant.Amount:0.00}.",
            });
            await guestSessionRepository.SaveAsync(session);
        }

        return await ComputePaymentAsync(group, sessions);
    }

    public async Task<PaymentView> MarkPaidAsync(string userId, string groupReservationId, string guestSessionId)
    {
        (GroupReservation group, _) = await LoadGroupForOwnerAsync(userId, groupReservationId);
        EnsureSubmittedForPayment(group);

        if (group.PaymentMode is null)
            throw new ValidationException("Choose how the group pays first.");

        List<GuestSession> sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId);
        GuestSession session = sessions.FirstOrDefault(s => s.Id == guestSessionId)
            ?? throw new GuestSessionNotFoundException($"Guest session with id {guestSessionId} not found.");

        session.IsPaid = true;
        session.PaidAt = UtcNow;
        await guestSessionRepository.SaveAsync(session);

        return await ComputePaymentAsync(group, sessions);
    }

    public async Task<GuestPaymentShare?> GetGuestPaymentShareAsync(string groupReservationId, string guestSessionId)
    {
        GroupReservation? group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId);
        if (group is null || group.Status != GroupReservationStatus.Submitted || group.PaymentMode is null)
            return null;

        List<GuestSession> sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId);
        PaymentView payment = await ComputePaymentAsync(group, sessions);
        PaymentParticipant? share = payment.Participants.FirstOrDefault(p => p.Id == guestSessionId);

        return share is null ? null : new GuestPaymentShare(group.PaymentMode, share.Amount, share.IsPaid, share.LinkSentAt);
    }

    public async Task<GuestPaymentShare> PayGuestShareAsync(string groupReservationId, string guestSessionId)
    {
        GroupReservation group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        EnsureSubmittedForPayment(group);
        if (group.PaymentMode is null)
            throw new ValidationException("The organizer has not chosen how the group pays yet.");

        List<GuestSession> sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId);
        PaymentView payment = await ComputePaymentAsync(group, sessions);
        PaymentParticipant share = payment.Participants.FirstOrDefault(p => p.Id == guestSessionId)
            ?? throw new ValidationException("You have nothing to pay in this group order.");

        if (!share.IsPaid)
        {
            GuestSession session = sessions.First(s => s.Id == guestSessionId);
            session.IsPaid = true;
            session.PaidAt = UtcNow;
            await guestSessionRepository.SaveAsync(session);
        }

        return new GuestPaymentShare(group.PaymentMode, share.Amount, true, share.LinkSentAt);
    }

    // ---------------------------------------------------------------- US21: book again with the same group

    public async Task<RebookResponse> RebookAsync(string userId, string reservationId, string previousReservationId)
    {
        if (string.Equals(reservationId, previousReservationId, StringComparison.Ordinal))
            throw new ValidationException("A reservation cannot be booked again from itself.");

        Reservation reservation = await reservationRepository.GetReservationByIdAsync(reservationId)
            ?? throw new ReservationNotFoundException($"Reservation {reservationId} was not found.");
        Reservation previous = await reservationRepository.GetReservationByIdAsync(previousReservationId)
            ?? throw new ReservationNotFoundException($"Reservation {previousReservationId} was not found.");

        EnsureOwner(reservation, userId);
        EnsureOwner(previous, userId);

        // US21 AC4: a cancelled reservation shows no order details and cannot be repeated.
        if (previous.Status == ReservationStatus.Cancelled)
            throw new ValidationException("A cancelled reservation cannot be booked again.");

        GroupReservation group = await groupReservationRepository.GetByReservationIdAsync(reservationId)
            ?? throw new ValidationException("The new reservation must be for 2 or more guests.");
        GroupReservation previousGroup = await groupReservationRepository.GetByReservationIdAsync(previousReservationId)
            ?? throw new GroupReservationNotFoundException();

        group.PreviousReservationId = previousReservationId;
        previousGroup.NextReservationId = reservationId;
        await groupReservationRepository.SaveGroupReservationAsync(group);
        await groupReservationRepository.SaveGroupReservationAsync(previousGroup);

        // AC3: everyone from the previous group sees the new invite when they open their old link.
        int invited = (await guestSessionRepository.GetAllByGroupReservationIdAsync(previousGroup.GroupReservationId))
            .Count(s => !s.IsOwnerSession);

        return new RebookResponse(group.GroupReservationId, group.ReservationToken, invited);
    }

    // ---------------------------------------------------------------- US13: cut-off and auto-send

    public async Task EnforceCutoffAsync(string groupReservationId)
    {
        GroupReservation? group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId);
        if (group is null)
            return;

        Reservation? reservation = await reservationRepository.GetReservationByIdAsync(group.ReservationId);
        if (reservation is null)
            return;

        await EnforceCutoffCoreAsync(group, reservation);
    }

    /// <summary>
    /// Runs on every read and write of a group: at the cut-off the pre-order is sent automatically (US13 AC5),
    /// and 30 minutes before it guests without dishes get an automatic reminder (US14 AC3).
    /// </summary>
    private async Task EnforceCutoffCoreAsync(GroupReservation group, Reservation reservation)
    {
        // A reservation without a start time (legacy data) has no cut-off.
        if (group.Status != GroupReservationStatus.Open
            || reservation.Status != ReservationStatus.Reserved
            || reservation.StartTime == default)
        {
            return;
        }

        DateTime now = UtcNow;

        if (GroupPreOrderRules.IsCutoffPassed(reservation, now))
        {
            List<GuestSession> sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId);
            await SubmitCoreAsync(group, reservation, sessions, reservation.CustomerId ?? string.Empty, automatic: true);
            return;
        }

        bool remindWindow = now >= GroupPreOrderRules.CutoffAtUtc(reservation).AddMinutes(-GroupPreOrderRules.AutoRemindMinutesBeforeCutoff);
        if (!remindWindow || group.AutoRemindDisabled || group.AutoRemindedAt is not null)
            return;

        List<GuestSession> guests = (await guestSessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId))
            .Where(s => !s.IsOwnerSession && (s.Dishes ?? []).Count == 0)
            .ToList();

        string ownerName = await GetOwnerDisplayNameAsync(reservation.CustomerId);
        foreach (GuestSession guest in guests)
        {
            AddNotice(guest, new GuestNotice
            {
                Type = GuestNoticeTypes.Reminder,
                ActorName = ownerName,
                CreatedAt = now,
                Message = $"Changes close soon. {ownerName} is waiting for your choice.",
            });
            guest.LastRemindedAt = now;
            await guestSessionRepository.SaveAsync(guest);
        }

        group.AutoRemindedAt = now;
        await groupReservationRepository.SaveGroupReservationAsync(group);
    }

    private async Task<IReadOnlyList<SubmittedDishResponse>> SubmitCoreAsync(
        GroupReservation group, Reservation reservation, List<GuestSession> sessions, string userId, bool automatic)
    {
        // The owner's own pre-order lives in the reservation order (POST /orders), not in the owner guest session.
        Order? order = await orderRepository.GetOrderByReservationIdAsync(reservation.Id);
        List<OrderItem> ownerItems = OwnerOrderItems(order) ?? [];

        List<GuestSession> submittedSessions = sessions.Where(s => s.IsSubmitted && !s.IsOwnerSession).ToList();

        if (!automatic && submittedSessions.Count == 0 && ownerItems.Count == 0)
            throw new ValidationException("There are no submitted guest sessions in this group reservation.");

        List<OrderItem> submittedItems = submittedSessions
            .SelectMany(s => s.Dishes ?? [])
            .Where(d => d.Quantity > 0)
            .ToList();

        if (!automatic && submittedItems.Count == 0 && ownerItems.Count == 0)
            throw new ValidationException("Submitted guest sessions do not contain any dishes.");

        // Dishes that went On Stop / Out of stock after guests chose them are not sent to the restaurant (US12 AC6, US13 AC5).
        Dictionary<string, Dish> dishes = await LoadDishesAsync(submittedItems.Concat(ownerItems).Select(i => i.DishId));
        bool IsAvailable(string dishId) => dishes.TryGetValue(dishId, out Dish? dish) && dish.State == DishState.Available;

        List<SubmittedDishResponse> merged = submittedItems
            .Where(i => IsAvailable(i.DishId))
            .GroupBy(i => i.DishId)
            .Select(g => new SubmittedDishResponse(g.Key, g.Sum(i => i.Quantity)))
            .ToList();

        if (!automatic)
        {
            if (merged.Count == 0 && ownerItems.Count == 0)
                throw new ValidationException("None of the dishes in submitted guest sessions are available.");

            if (order is null)
                throw new ValidationException($"Order for reservation {reservation.Id} was not found.");

            if (order.Status == OrderStatus.Cancelled)
                throw new ValidationException($"Order for reservation {reservation.Id} was cancelled.");
        }

        // After the merge the order also holds the guests' dishes, so keep the owner's own part in the owner session.
        GuestSession ownerSession = await SaveOwnerSnapshotAsync(sessions, group, userId, ownerItems);

        bool canMerge = order is not null && order.Status != OrderStatus.Cancelled && (merged.Count > 0 || ownerItems.Count > 0);
        if (canMerge)
        {
            order!.Dishes ??= [];
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

            // US18 AC5 / US19: who ordered what, with notes, serving times and allergies — for the kitchen.
            string ownerName = await GetOwnerDisplayNameAsync(reservation.CustomerId);
            var participants = new List<(string Name, IEnumerable<OrderItem> Items, string? Allergies)>
            {
                (ownerName, ownerItems, ownerSession.Allergies),
            };
            participants.AddRange(submittedSessions.Select(s => (s.GuestName, (IEnumerable<OrderItem>)(s.Dishes ?? []), s.Allergies)));

            order.KitchenNotes = participants
                .SelectMany(p => p.Items
                    .Where(i => i.Quantity > 0 && IsAvailable(i.DishId))
                    .Select(i => new KitchenNote
                    {
                        DishId = i.DishId,
                        DishName = dishes[i.DishId].Name,
                        ParticipantName = p.Name,
                        Quantity = i.Quantity,
                        Note = i.Note,
                        ServeTime = i.ServeTime ?? GroupPreOrderRules.DefaultServeTime(dishes[i.DishId].DishType),
                    }))
                .ToList();

            order.Allergies = participants
                .Where(p => !string.IsNullOrWhiteSpace(p.Allergies))
                .Select(p => new ParticipantAllergy { ParticipantName = p.Name, Allergies = p.Allergies! })
                .ToList();

            order.Status = OrderStatus.SubmittedByCustomer;
            await orderRepository.SaveOrderAsync(order);
        }

        group.Status = GroupReservationStatus.Submitted;
        group.SubmittedAt = UtcNow;
        group.SubmittedAutomatically = automatic;
        await groupReservationRepository.SaveGroupReservationAsync(group);

        return merged;
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

    /// <summary>Owner + group still open and before the cut-off (US13 AC3, US15 AC5).</summary>
    private async Task<(GroupReservation Group, Reservation Reservation)> LoadOpenGroupForOwnerAsync(string userId, string groupReservationId)
    {
        (GroupReservation group, Reservation reservation) = await LoadGroupForOwnerAsync(userId, groupReservationId);
        await EnforceCutoffCoreAsync(group, reservation);
        EnsureGroupIsOpen(group);
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

    private static void EnsureSubmittedForPayment(GroupReservation group)
    {
        if (group.Status != GroupReservationStatus.Submitted)
            throw new ValidationException("Payment is available after the pre-order is sent to the restaurant.");
    }

    private static bool IsRemovalNotice(GuestNotice notice)
        => notice.Type == GuestNoticeTypes.DishRemoved || (notice.Type is null && notice.DishId is not null);

    private static void AddNotice(GuestSession session, GuestNotice notice)
    {
        session.Notices ??= [];
        session.Notices.Add(notice);
    }

    /// <summary>Null keeps the current value; an empty string clears it.</summary>
    internal static string? NormalizeText(string? value, int maxLength, string field)
    {
        if (value is null)
            return null;

        string trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new ValidationException($"{field} must be at most {maxLength} characters.");

        return trimmed;
    }

    internal static string? NormalizeServeTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!ServeTimes.IsValid(value))
            throw new ValidationException($"Serve time must be {ServeTimes.OnArrival} or {ServeTimes.AfterMains}.");

        return value;
    }

    internal static void ApplyDetails(OrderItem item, string? note, string? serveTime)
    {
        if (note is not null)
            item.Note = note.Length == 0 ? null : note;

        if (serveTime is not null)
            item.ServeTime = serveTime;
    }

    private async Task<Dictionary<string, Dish>> LoadDishesAsync(IEnumerable<string> ids)
    {
        string[] dishIds = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
        if (dishIds.Length == 0)
            return [];

        IEnumerable<Dish> dishes = await dishRepository.GetDishesByIdsAsync(dishIds);
        return dishes
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.Id))
            .GroupBy(d => d.Id)
            .ToDictionary(g => g.Key, g => g.First());
    }

    private static decimal TotalOf(IEnumerable<OrderItem> items, IReadOnlyDictionary<string, Dish> dishes)
        => items
            .Where(i => i.Quantity > 0 && dishes.TryGetValue(i.DishId, out Dish? dish) && dish.State == DishState.Available)
            .Sum(i => dishes[i.DishId].Price * i.Quantity);

    private async Task<GetAllSessionsResponse> SnapshotAsync(GroupReservation group, Reservation reservation)
    {
        List<GuestSession> sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId);
        return await BuildSnapshotAsync(sessions, group, reservation);
    }

    /// <summary>Snapshot that uses the just-saved copy of one session in case the index read is stale.</summary>
    private async Task<GetAllSessionsResponse> SnapshotWithAsync(GuestSession saved, GroupReservation group, Reservation reservation)
    {
        List<GuestSession> sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId);
        List<GuestSession> snapshotSessions = sessions.Select(s => s.Id == saved.Id ? saved : s).ToList();
        if (snapshotSessions.All(s => s.Id != saved.Id))
            snapshotSessions.Add(saved);

        return await BuildSnapshotAsync(snapshotSessions, group, reservation);
    }

    private async Task<GetAllSessionsResponse> BuildSnapshotAsync(
        IReadOnlyCollection<GuestSession> sessions, GroupReservation group, Reservation reservation)
    {
        // Owner first (US12 AC2), then guests in their stored order.
        List<GuestSession> ordered = sessions
            .OrderByDescending(s => s.IsOwnerSession)
            .ToList();

        // While the group is open, the owner's dishes come from the live reservation order.
        // After submit they come from the snapshot saved in the owner session (the order then includes the guests' dishes).
        List<OrderItem>? ownerOrderItems = group.Status == GroupReservationStatus.Open && ordered.Any(s => s.IsOwnerSession)
            ? OwnerOrderItems(await orderRepository.GetOrderByReservationIdAsync(reservation.Id))
            : null;

        List<OrderItem> ItemsOf(GuestSession session) =>
            session.IsOwnerSession && ownerOrderItems is not null ? ownerOrderItems : session.Dishes ?? [];

        bool IsSubmitted(GuestSession session) =>
            session.IsOwnerSession && ownerOrderItems is not null ? ownerOrderItems.Count > 0 : session.IsSubmitted;

        Dictionary<string, Dish> dishesById = await LoadDishesAsync(ordered.SelectMany(ItemsOf).Select(d => d.DishId));

        string? ownerName = ordered.Any(s => s.IsOwnerSession)
            ? await GetOwnerDisplayNameAsync(reservation.CustomerId)
            : null;

        var participants = new List<Participant>();
        foreach (GuestSession session in ordered)
        {
            // Dishes deleted from the menu are skipped instead of failing the whole snapshot.
            List<(OrderItem Item, Dish Dish)> lines = ItemsOf(session)
                .Where(d => d.Quantity > 0 && dishesById.ContainsKey(d.DishId))
                .Select(d => (d, dishesById[d.DishId]))
                .ToList();

            OrderItemResponse[] items = lines
                .Select(l => new OrderItemResponse
                {
                    Dish = DishMapper.ToDishResponse(l.Dish),
                    Quantity = l.Item.Quantity,
                    Note = l.Item.Note,
                    ServeTime = l.Item.ServeTime ?? GroupPreOrderRules.DefaultServeTime(l.Dish.DishType),
                })
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
                IsSubmitted(session))
            {
                Allergies = session.Allergies,
                ChangedByOwner = session.ChangedByOwner,
                LastRemindedAt = session.LastRemindedAt,
                IsPaid = session.IsPaid,
            });
        }

        List<DishSummary> summary = ordered
            .SelectMany(ItemsOf)
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

        DateTime now = UtcNow;
        bool hasStartTime = reservation.StartTime != default;
        bool cutoffPassed = hasStartTime && GroupPreOrderRules.IsCutoffPassed(reservation, now);

        return new GetAllSessionsResponse(participants, participants.Sum(p => p.Total))
        {
            SummaryByDish = summary,
            HasUnavailableDishes = summary.Any(s => !s.IsAvailable),
            GroupStatus = group.Status.ToString(),
            CutoffAt = hasStartTime ? GroupPreOrderRules.CutoffAt(reservation) : null,
            SecondsUntilCutoff = hasStartTime ? GroupPreOrderRules.SecondsUntilCutoff(reservation, now) : 0,
            IsLocked = group.Status != GroupReservationStatus.Open || cutoffPassed,
            SubmittedAt = group.SubmittedAt,
            SubmittedAutomatically = group.SubmittedAutomatically,
            AutoRemind = !group.AutoRemindDisabled,
            PaymentMode = group.PaymentMode,
            MaxGuests = reservation.GuestCount,
            ReservationStatus = reservation.Status.ToString(),
        };
    }

    /// <summary>
    /// US17: shares of the owner and of every guest whose cart went to the restaurant.
    /// Split equally rounds each guest's share down to cents; the rounding difference goes to the organizer (AC2).
    /// </summary>
    private async Task<PaymentView> ComputePaymentAsync(GroupReservation group, IReadOnlyCollection<GuestSession> sessions)
    {
        GuestSession? ownerSession = sessions.FirstOrDefault(s => s.IsOwnerSession);
        List<GuestSession> payers = sessions
            .Where(s => !s.IsOwnerSession && s.IsSubmitted)
            .ToList();

        Dictionary<string, Dish> dishes = await LoadDishesAsync(
            sessions.Where(s => s.IsOwnerSession || s.IsSubmitted).SelectMany(s => s.Dishes ?? []).Select(d => d.DishId));

        var rows = new List<(GuestSession? Session, ParticipantType Type, string Name, decimal Own)>
        {
            (ownerSession, ParticipantType.Owner, ownerSession is null ? OwnerFallbackName : await GetOwnerNameForSessionAsync(group, ownerSession),
                TotalOf(ownerSession?.Dishes ?? [], dishes)),
        };
        rows.AddRange(payers.Select(s => ((GuestSession?)s, ParticipantType.Guest, s.GuestName, TotalOf(s.Dishes ?? [], dishes))));

        decimal grandTotal = rows.Sum(r => r.Own);
        decimal[] amounts = group.PaymentMode switch
        {
            PaymentModes.OwnerPaysAll => rows.Select(r => r.Type == ParticipantType.Owner ? grandTotal : 0m).ToArray(),
            PaymentModes.SplitEqually => SplitEqually(grandTotal, rows.Count),
            _ => rows.Select(r => r.Own).ToArray(),
        };

        List<PaymentParticipant> participants = rows
            .Select((r, index) => new PaymentParticipant(
                r.Session?.Id ?? string.Empty,
                r.Type,
                r.Name,
                r.Own,
                amounts[index],
                amounts[index] == 0m || (r.Session?.IsPaid ?? false),
                r.Session?.PaidAt,
                r.Session?.PaymentLinkSentAt))
            .ToList();

        decimal paid = participants.Where(p => p.IsPaid).Sum(p => p.Amount);
        return new PaymentView(group.PaymentMode, participants, grandTotal, paid, grandTotal - paid);
    }

    /// <summary>Equal shares rounded down to cents; index 0 (the organizer) takes the remainder.</summary>
    public static decimal[] SplitEqually(decimal total, int count)
    {
        if (count <= 0)
            return [];

        decimal share = Math.Floor(total * 100m / count) / 100m;
        decimal[] amounts = Enumerable.Repeat(share, count).ToArray();
        amounts[0] = total - share * (count - 1);
        return amounts;
    }

    private async Task<string> GetOwnerNameForSessionAsync(GroupReservation group, GuestSession ownerSession)
    {
        Reservation? reservation = await reservationRepository.GetReservationByIdAsync(group.ReservationId);
        return reservation is null ? ownerSession.GuestName : await GetOwnerDisplayNameAsync(reservation.CustomerId);
    }

    /// <summary>Owner's own pre-order lines, or null when there is no active order.</summary>
    private static List<OrderItem>? OwnerOrderItems(Order? order)
    {
        if (order is null || order.Status == OrderStatus.Cancelled)
            return null;

        return (order.Dishes ?? [])
            .Where(d => d.Quantity > 0 && d.State != OrderItemState.Canceled)
            .Select(d => new OrderItem { DishId = d.DishId, Quantity = d.Quantity, State = d.State, Note = d.Note, ServeTime = d.ServeTime })
            .ToList();
    }

    private async Task<GuestSession> GetOrCreateOwnerSessionAsync(IReadOnlyCollection<GuestSession> sessions, GroupReservation group, string userId)
    {
        GuestSession? ownerSession = sessions.FirstOrDefault(s => s.IsOwnerSession && s.OwnerUserId == userId)
            ?? sessions.FirstOrDefault(s => s.IsOwnerSession);

        if (ownerSession is not null)
            return ownerSession;

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
        return ownerSession;
    }

    private async Task<GuestSession> SaveOwnerSnapshotAsync(
        IReadOnlyCollection<GuestSession> sessions, GroupReservation group, string userId, List<OrderItem> ownerItems)
    {
        GuestSession ownerSession = sessions.FirstOrDefault(s => s.IsOwnerSession && s.OwnerUserId == userId)
            ?? sessions.FirstOrDefault(s => s.IsOwnerSession)
            ?? new GuestSession
            {
                Id = Guid.NewGuid().ToString(),
                GroupReservationId = group.GroupReservationId,
                GuestName = OwnerFallbackName,
                Dishes = [],
                IsOwnerSession = true,
                OwnerUserId = userId,
            };

        ownerSession.Dishes = ownerItems;
        ownerSession.IsSubmitted = ownerItems.Count > 0;
        await guestSessionRepository.SaveAsync(ownerSession);
        return ownerSession;
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
