import { Injectable, computed, effect, signal, inject } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { GuestInviteService } from '@app/services/reservation/guest-invite.service';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GuestMenuDish } from '@app/services/guest-sessions/guest-sessions.service';
import { GuestSessionsService } from '@app/services/guest-sessions/guest-sessions.service';
import { ServeTime } from '@app/models/group-orders/participant.model';
import {
    GuestNotice,
    GuestPaymentShare,
    GuestRebook,
    GuestSessionInfo,
} from '@app/models/group-orders/guest-session.model';
import { defaultServeTime, formatTimeLeft } from '@app/shared/helpers/group-pre-order';

export type GuestPreorderStatus = 'active' | 'submitted';

export interface GuestInviteVm {
    inviterName: string;
    restaurantName: string;
    locationAddress: string;
    tableNumberText: string;
    dateText: string;
    timeText: string;
    guestsText: string;
}

export interface GuestCartItem {
    dishId: string;
    title: string;
    price: number;
    imageUrl?: string;
    qty: number;
    /** US18: note for the kitchen. */
    note?: string | null;
    /** US19: serving time (the server fills in the default). */
    serveTime?: ServeTime;
    dishType?: string;
}

@Injectable({ providedIn: 'root' })
export class GuestPreorderStore {
    private readonly contextId = signal<string | null>(null);
    private readonly guestInviteService = inject(GuestInviteService);

    readonly invite = signal<GuestInvite | null>(null);
    readonly inviteLoading = signal(false);
    readonly inviteError = signal<'expired' | 'retryable' | null>(null);

    readonly guestName = signal<string>('');
    readonly status = signal<GuestPreorderStatus>('active');
    readonly cartItems = signal<GuestCartItem[]>([]);

    private readonly guestSessionsService = inject(GuestSessionsService);

    readonly joinLoading = signal(false);
    readonly joinError = signal<string | null>(null);

    readonly guestSessionId = signal<string | null>(null);
    readonly guestSessionToken = signal<string | null>(null);
    readonly menuLoading = signal(false);
    readonly menuError = signal<string | null>(null);
    readonly cartBusy = signal(false);
    readonly cartError = signal<string | null>(null);

    // ---- US13–US21: what the server says about this guest
    readonly notices = signal<GuestNotice[]>([]);
    readonly allergies = signal('');
    readonly ownerName = signal<string | null>(null);
    readonly isLocked = signal(false);
    readonly groupStatus = signal<string | null>(null);
    readonly payment = signal<GuestPaymentShare | null>(null);
    readonly rebook = signal<GuestRebook | null>(null);
    /** US16 AC5: the organizer removed this guest from the group. */
    readonly removed = signal(false);
    readonly cutoffAt = signal<string | null>(null);
    private readonly cutoff = signal<{ seconds: number; receivedAt: number } | null>(null);

    readonly menuByCategory = signal<Record<string, GuestMenuDish[]>>({});
    readonly menuCategories = computed(() => Object.keys(this.menuByCategory()));
    readonly hasMenu = computed(() =>
        Object.values(this.menuByCategory()).some(dishes => dishes.length > 0)
    );

    readonly total = computed(() =>
        this.cartItems().reduce((sum, i) => sum + i.price * i.qty, 0)
    );

    /** US16 AC1/AC2: dishes the organizer removed (not yet acknowledged). */
    readonly removedNotices = computed(() => this.notices().filter(n => n.type === 'DishRemoved'));
    /** US14 / US15 AC4 / US17: other messages from the organizer. */
    readonly messageNotices = computed(() => this.notices().filter(n => n.type !== 'DishRemoved'));

    constructor() {
        effect(() => {
            const ctx = this.contextId();
            if (!ctx) return;

            localStorage.setItem(this.key(ctx, 'guestName'), this.guestName());
            localStorage.setItem(this.key(ctx, 'cart'), JSON.stringify(this.cartItems()));
            localStorage.setItem(this.key(ctx, 'guestSessionId'), this.guestSessionId() ?? '');
            localStorage.setItem(this.key(ctx, 'guestSessionToken'), this.guestSessionToken() ?? '');
        });
    }

    /** The guest already joined on this device — the page must not redirect to "expired". */
    hasStoredSession(): boolean {
        return !!this.guestSessionId() && !!this.guestSessionToken();
    }

    initContext(id: string) {
        if (!id) return;
        if (this.contextId() === id) return;

        this.contextId.set(id);

        const name = localStorage.getItem(this.key(id, 'guestName'));
        const cartRaw = localStorage.getItem(this.key(id, 'cart'));

        const sessionId = localStorage.getItem(this.key(id, 'guestSessionId'));
        const sessionToken = localStorage.getItem(this.key(id, 'guestSessionToken'));

        // A different invite (e.g. "Join the new pre-order", US21) starts from a clean state.
        this.guestSessionId.set(sessionId || null);
        this.guestSessionToken.set(sessionToken || null);
        this.guestName.set('');
        this.cartItems.set([]);
        this.notices.set([]);
        this.status.set('active');
        this.isLocked.set(false);
        this.payment.set(null);
        this.rebook.set(null);
        this.removed.set(false);
        this.cutoff.set(null);
        this.menuByCategory.set({});

        void this.loadInviteByToken(id);

        // If guest was already joined on this device/browser, load menu and the server cart automatically
        if (this.guestSessionId() && this.guestSessionToken()) {
            void this.loadMenu();
            void this.syncSession();
        }

        if (name) this.guestName.set(name);

        if (cartRaw) {
            try {
                const parsed = JSON.parse(cartRaw);
                if (Array.isArray(parsed)) this.cartItems.set(parsed);
            } catch {
                // ignore parse errors
            }
        }
    }

    async loadInviteByToken(token: string) {
        this.inviteLoading.set(true);
        this.inviteError.set(null);

        try {
            const inv = await firstValueFrom(this.guestInviteService.getInvite$(token));
            this.invite.set(inv);
            localStorage.setItem(this.key(token, 'invite'), JSON.stringify(inv));
        } catch (error) {
            // After the cut-off or submit the invite is closed for guests; a guest who already joined
            // still sees their own order with the details remembered on this device (US13 AC4).
            const cached = this.readCachedInvite(token);
            this.invite.set(cached);
            const status = error instanceof HttpErrorResponse ? error.status : null;
            if (cached && this.hasStoredSession()) {
                this.inviteError.set(null);
            } else {
                this.inviteError.set(status === 404 || status === 410 ? 'expired' : 'retryable');
            }
        } finally {
            this.inviteLoading.set(false);
        }
    }

    async join(name: string) {
        const inv = this.invite();
        if (!inv) {
            this.joinError.set('Invite is not loaded yet');
            return;
        }

        this.setGuestName(name);

        const groupReservationId = inv.GroupReservationId;
        if (!groupReservationId) {
            this.joinError.set('Invite is missing GroupReservationId');
            return;
        }

        const payload = {
            groupReservationId,
            guestName: this.guestName(),
        };

        this.joinLoading.set(true);
        this.joinError.set(null);

        try {
            const res = await firstValueFrom(this.guestSessionsService.createSession(payload));
            this.guestSessionId.set(res.SessionId);
            this.guestSessionToken.set(res.SessionToken);
            await this.loadMenu();
            await this.syncSession();
        } catch {
            this.joinError.set('Failed to join session');
        } finally {
            this.joinLoading.set(false);
        }
    }

    readonly inviteVm = computed<GuestInviteVm | null>(() => {
        const inv = this.invite();
        if (!inv) return null;

        const inviterName = inv.InviterName ?? 'Someone';
        const restaurantName = inv.RestaurantName ?? '—';
        const locationAddress = inv.LocationAddress ?? '—';

        const tableNumberText =
            inv.TableNumber != null && String(inv.TableNumber).trim() !== ''
                ? `Table ${inv.TableNumber}`
                : '—';

        const startText = inv.StartTime ? String(inv.StartTime) : '';
        const endText = inv.EndTime ? String(inv.EndTime) : '';

        const dateText = startText
            ? new Date(startText).toLocaleDateString('en-US', { month: 'short', day: '2-digit', year: 'numeric' })
            : '—';

        const timeText = (startText && endText)
            ? `${new Date(startText).toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', hour12: false })} - ${new Date(endText).toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', hour12: false })}`
            : '—';

        const guestsText = inv.MaxGuestCount != null ? `${inv.MaxGuestCount} people` : '—';

        return { inviterName, restaurantName, locationAddress, tableNumberText, dateText, timeText, guestsText };
    });

    readonly inviteText = computed(() => {
        const vm = this.inviteVm();
        if (!vm) return null;

        const parts = [vm.locationAddress, vm.tableNumberText, vm.timeText].filter(Boolean);
        return `You are making a pre-order for the reservation ${parts.join(', ')}`;
    });

    /** US13 AC1: "in 1h 20m" from the server value; refreshed with every sync. */
    readonly timeLeft = computed(() => {
        const cutoff = this.cutoff();
        return cutoff ? formatTimeLeft(cutoff.seconds) : null;
    });

    async loadMenu() {
        const id = this.guestSessionId();
        const token = this.guestSessionToken();
        if (!id || !token) return;

        this.menuLoading.set(true);
        this.menuError.set(null);

        try {
            const res = await firstValueFrom(this.guestSessionsService.getMenu(id, token));
            this.menuByCategory.set(res.dishes ?? {});
        } catch {
            this.menuError.set('Failed to load menu');
            this.menuByCategory.set({});
        } finally {
            this.menuLoading.set(false);
        }
    }

    /**
     * The server is the source of truth for the cart: the organizer may have removed a dish (US16),
     * asked for a change (US15), sent the order at the cut-off (US13) or asked for payment (US17).
     */
    async syncSession(): Promise<void> {
        const id = this.guestSessionId();
        const token = this.guestSessionToken();
        if (!id || !token) return;

        try {
            const info = await firstValueFrom(this.guestSessionsService.getSession(id, token));
            this.applySession(info);
        } catch (error) {
            const status = error instanceof HttpErrorResponse ? error.status : null;
            if (status === 401 || status === 404) {
                await this.checkRemoved(id);
            }
        }
    }

    private applySession(info: GuestSessionInfo): void {
        if (info.name) this.guestName.set(info.name);
        this.cartItems.set(info.items.map(item => ({
            dishId: item.dish.id,
            title: item.dish.name,
            price: item.dish.price,
            imageUrl: item.dish.imageUrl ?? undefined,
            qty: item.quantity,
            note: item.note ?? null,
            serveTime: item.serveTime ?? defaultServeTime(item.dish.dishType),
            dishType: item.dish.dishType,
        })));
        this.status.set(info.isSubmitted ? 'submitted' : 'active');
        this.notices.set(info.notices ?? []);
        this.allergies.set(info.allergies ?? '');
        this.ownerName.set(info.ownerName ?? null);
        this.isLocked.set(!!info.isLocked);
        this.groupStatus.set(info.groupStatus ?? null);
        this.payment.set(info.payment ?? null);
        this.rebook.set(info.rebook ?? null);
        this.cutoffAt.set(info.cutoffAt ?? null);
        this.cutoff.set(info.cutoffAt ? { seconds: info.secondsUntilCutoff ?? 0, receivedAt: Date.now() } : null);
        this.removed.set(false);
    }

    private async checkRemoved(guestSessionId: string): Promise<void> {
        const groupId = (this.invite() ?? this.readCachedInvite(this.contextId() ?? ''))?.GroupReservationId;
        if (!groupId) return;
        try {
            const status = await firstValueFrom(this.guestSessionsService.getGroupStatus(groupId, guestSessionId));
            this.removed.set(!!status.removed);
        } catch {
            // keep the last known state
        }
    }

    setGuestName(name: string) {
        this.guestName.set(name.trim());
    }

    async addDish(item: Omit<GuestCartItem, 'qty'>): Promise<boolean> {
        return this.changeRemoteQuantity(item.dishId, 1, 'add', () => {
            const items = this.cartItems();
            const existing = items.find(cartItem => cartItem.dishId === item.dishId);
            this.cartItems.set(existing
                ? items.map(cartItem => cartItem.dishId === item.dishId
                    ? { ...cartItem, qty: cartItem.qty + 1 }
                    : cartItem)
                : [...items, { ...item, qty: 1 }]);
        });
    }

    async inc(dishId: string): Promise<boolean> {
        return this.changeRemoteQuantity(dishId, 1, 'add', () => {
            this.cartItems.set(this.cartItems().map(item =>
                item.dishId === dishId ? { ...item, qty: item.qty + 1 } : item
            ));
        });
    }

    async dec(dishId: string): Promise<boolean> {
        return this.changeRemoteQuantity(dishId, 1, 'remove', () => {
            this.cartItems.set(this.cartItems()
                .map(item => item.dishId === dishId ? { ...item, qty: item.qty - 1 } : item)
                .filter(item => item.qty > 0));
        });
    }

    async remove(dishId: string): Promise<boolean> {
        const item = this.cartItems().find(cartItem => cartItem.dishId === dishId);
        if (!item) return false;

        return this.changeRemoteQuantity(dishId, item.qty, 'remove', () => {
            this.cartItems.set(this.cartItems().filter(cartItem => cartItem.dishId !== dishId));
        });
    }

    // ---- US18/US19

    async setNote(dishId: string, note: string): Promise<void> {
        await this.runGuestAction(
            (id, token) => this.guestSessionsService.updateDish(id, token, dishId, { note: note.trim() }),
            'Could not save the note. Please try again.',
        );
    }

    async setServeTime(dishId: string, serveTime: ServeTime): Promise<void> {
        await this.runGuestAction(
            (id, token) => this.guestSessionsService.updateDish(id, token, dishId, { serveTime }),
            'Could not change the serving time. Please try again.',
        );
    }

    async setAllergies(allergies: string): Promise<void> {
        await this.runGuestAction(
            (id, token) => this.guestSessionsService.updateAllergies(id, token, allergies.trim()),
            'Could not save allergies. Please try again.',
        );
    }

    // ---- US16

    async acknowledgeNotices(): Promise<void> {
        await this.runGuestAction(
            (id, token) => this.guestSessionsService.acknowledgeNotices(id, token),
            'Could not update. Please try again.',
        );
    }

    /** "Pick another dish": a submitted cart becomes editable again (before the cut-off). */
    async reopen(): Promise<boolean> {
        if (this.status() !== 'submitted') return true;
        return this.runGuestAction(
            (id, token) => this.guestSessionsService.reopen(id, token),
            'The order can no longer be changed.',
        );
    }

    // ---- US17

    async pay(): Promise<boolean> {
        return this.runGuestAction(
            (id, token) => this.guestSessionsService.pay(id, token),
            'Payment failed. Please try again.',
        );
    }

    /** Calls a guest endpoint, then reloads the session from the server. */
    private async runGuestAction(
        request: (id: string, token: string) => ReturnType<GuestSessionsService['reopen']>,
        errorMessage: string,
    ): Promise<boolean> {
        const sessionId = this.guestSessionId();
        const token = this.guestSessionToken();
        if (!sessionId || !token) {
            this.cartError.set('Your guest session has expired. Please rejoin the reservation.');
            return false;
        }

        this.cartBusy.set(true);
        this.cartError.set(null);
        try {
            await firstValueFrom(request(sessionId, token));
            await this.syncSession();
            return true;
        } catch {
            this.cartError.set(errorMessage);
            return false;
        } finally {
            this.cartBusy.set(false);
        }
    }

    async submitPreorder() {
        const sessionId = this.guestSessionId();
        const token = this.guestSessionToken();
        if (!sessionId || !token) {
            this.cartError.set('Your guest session has expired. Please rejoin the reservation.');
            return;
        }
        if (this.cartItems().length === 0 || this.cartBusy()) return;

        this.cartBusy.set(true);
        this.cartError.set(null);
        try {
            await firstValueFrom(this.guestSessionsService.submit(sessionId, token));
            this.submitSuccess();
        } catch {
            this.cartError.set('Could not submit your pre-order. Please try again.');
        } finally {
            this.cartBusy.set(false);
        }
    }

    // call this only after API submit success
    submitSuccess() {
        // The session is kept: the guest can come back to the link and see changes made by the organizer (US16).
        this.status.set('submitted');
    }

    async createNewPreorder() {
        const ctx = this.contextId();
        const invite = this.invite();
        const guestName = this.guestName().trim();
        if (!ctx || !invite?.GroupReservationId || !guestName || this.joinLoading()) return;

        localStorage.removeItem(this.key(ctx, 'cart'));
        localStorage.removeItem(this.key(ctx, 'guestSessionId'));
        localStorage.removeItem(this.key(ctx, 'guestSessionToken'));

        this.guestSessionId.set(null);
        this.guestSessionToken.set(null);
        this.menuByCategory.set({});
        this.menuError.set(null);
        this.menuLoading.set(true);
        this.cartItems.set([]);
        this.notices.set([]);
        this.status.set('active');
        this.joinLoading.set(true);

        try {
            const session = await firstValueFrom(this.guestSessionsService.createSession({
                groupReservationId: invite.GroupReservationId,
                guestName,
            }));
            this.guestSessionId.set(session.SessionId);
            this.guestSessionToken.set(session.SessionToken);
            await this.loadMenu();
        } catch {
            this.menuError.set('Could not start a new pre-order. Please try again.');
        } finally {
            this.menuLoading.set(false);
            this.joinLoading.set(false);
        }
    }

    private async changeRemoteQuantity(
        dishId: string,
        quantity: number,
        operation: 'add' | 'remove',
        updateLocalCart: () => void,
    ): Promise<boolean> {
        const sessionId = this.guestSessionId();
        const token = this.guestSessionToken();
        if (!sessionId || !token) {
            this.cartError.set('Your guest session has expired. Please rejoin the reservation.');
            return false;
        }
        if (this.cartBusy()) return false;

        this.cartBusy.set(true);
        this.cartError.set(null);
        try {
            const request = operation === 'add'
                ? this.guestSessionsService.addDish(sessionId, token, dishId, quantity)
                : this.guestSessionsService.removeDish(sessionId, token, dishId, quantity);
            await firstValueFrom(request);
            updateLocalCart();
            void this.syncSession();
            return true;
        } catch (error) {
            const status = error instanceof HttpErrorResponse ? error.status : null;
            this.cartError.set(status === 409
                ? 'The order was sent to the restaurant — contact the organizer to change it.'
                : 'Could not update your cart. Please try again.');
            if (status === 409) void this.syncSession();
            return false;
        } finally {
            this.cartBusy.set(false);
        }
    }

    private readCachedInvite(token: string): GuestInvite | null {
        try {
            const raw = localStorage.getItem(this.key(token, 'invite'));
            return raw ? (JSON.parse(raw) as GuestInvite) : null;
        } catch {
            return null;
        }
    }

    private key(
        ctx: string,
        part: 'guestName' | 'cart' | 'status' | 'guestSessionId' | 'guestSessionToken' | 'invite'
    ) {
        return `guestPreorder:${ctx}:${part}`;
    }

}
