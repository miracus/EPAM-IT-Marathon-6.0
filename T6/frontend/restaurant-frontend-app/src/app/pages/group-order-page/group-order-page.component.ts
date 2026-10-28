import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  EMPTY,
  Observable,
  Subscription,
  catchError,
  defer,
  exhaustMap,
  finalize,
  forkJoin,
  interval,
  map,
  of,
  switchMap,
  takeWhile,
  tap,
} from 'rxjs';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { ToastService } from '@app/services/toast/toast.service';
import { Participant } from '@app/models/group-orders/participant.model';
import { GroupReservationStatus } from '@app/models/group-orders/group-status.model';
import { GroupInvite } from '@app/models/group-orders/group-invite.model';
import { GroupSessionsResponse } from '@app/models/group-orders/group-sessions-response.model';
import { PaymentMode, PaymentParticipant, PaymentView } from '@app/models/group-orders/group-payment.model';
import { DishDetailsChange, DishRemoval, RemovedLine, RemindersResponse } from '@app/models/group-orders/group-actions.model';
import { consolidatePreOrder } from '@app/shared/helpers/consolidate-pre-order';
import { findNewItems, formatTimeLeft, guestsWithoutDishes, progressLabel } from '@app/shared/helpers/group-pre-order';
import { GROUP_ORDER_SYNC_INTERVAL } from '@app/shared/config/group-order-sync.token';
import { ParticipantCardComponent } from '@app/components/group-orders/participant-card/participant-card.component';
import { KitchenViewComponent } from '@app/components/group-orders/kitchen-view/kitchen-view.component';
import { PaymentPanelComponent } from '@app/components/group-orders/payment-panel/payment-panel.component';
import { ModalComponent } from '@app/shared/custom-components/modal/modal.component';
import { GroupOrderMeta, GroupOrderSnapshot, GroupOrderSync, GroupOrderView, HighlightedLine, LoadState } from './models';

/** Default sync period of the homework (level 3); the app provides 5 s through GROUP_ORDER_SYNC_INTERVAL (US20). */
export const GROUP_ORDER_SYNC_MS = 15_000;

/** US20 AC2: a new line stays highlighted for 10 seconds. */
const NEW_HIGHLIGHT_MS = 10_000;

const STATUS_LABELS: Record<GroupReservationStatus, string> = {
  Open: 'Collecting pre-orders',
  Submitted: 'Sent to Restaurant',
  Closed: 'Closed',
};

const EMPTY_META: GroupOrderMeta = {
  cutoffAt: null,
  secondsUntilCutoff: 0,
  receivedAt: 0,
  isLocked: false,
  submittedAt: null,
  submittedAutomatically: false,
  autoRemind: true,
  paymentMode: null,
  maxGuests: null,
  reservationStatus: null,
};

@Component({
  selector: 'app-group-order-page',
  imports: [
    CurrencyPipe,
    DatePipe,
    RouterLink,
    ParticipantCardComponent,
    KitchenViewComponent,
    PaymentPanelComponent,
    ModalComponent,
  ],
  templateUrl: './group-order-page.component.html',
  styleUrl: './group-order-page.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class GroupOrderPageComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly groupOrders = inject(GroupOrdersService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly syncInterval = inject(GROUP_ORDER_SYNC_INTERVAL);

  readonly reservationId = this.route.snapshot.paramMap.get('reservationId') ?? '';

  // ---- state
  readonly loadState = signal<LoadState>('loading');
  readonly invite = signal<GroupInvite | null>(null);
  readonly ownerSessionId = signal('');
  readonly participants = signal<Participant[]>([]);
  readonly status = signal<GroupReservationStatus>('Open');
  readonly meta = signal<GroupOrderMeta>(EMPTY_META);
  readonly view = signal<GroupOrderView>('guests');

  readonly lastUpdated = signal<Date | null>(null);
  /** "Now" for the countdown and NEW tags; moves on every sync so the view stays stable between renders. */
  private readonly clock = signal(Date.now());
  readonly refreshFailed = signal(false);
  readonly refreshing = signal(false);

  readonly submitDialogOpen = signal(false);
  readonly submitting = signal(false);

  readonly removeTarget = signal<Participant | null>(null);
  readonly removing = signal(false);

  /** US15 AC2: dishes removed by the owner, shown with Undo until the next change. */
  readonly removedLines = signal<RemovedLine[]>([]);
  /** US20 AC2: lines added by guests since the previous sync. */
  readonly highlighted = signal<HighlightedLine[]>([]);
  readonly busy = signal(false);

  /** US17 */
  readonly paymentOpen = signal(false);
  readonly payment = signal<PaymentView | null>(null);
  readonly paymentBusy = signal(false);

  // ---- derived values
  /** Order for the restaurant: submitted carts only, identical dishes merged into one line. */
  readonly consolidated = computed(() => consolidatePreOrder(this.participants()));
  readonly grandTotal = computed(() => this.consolidated().total);
  readonly dishCount = computed(() => this.consolidated().items.reduce((sum, item) => sum + item.quantity, 0));

  readonly isOpen = computed(() => this.status() === 'Open');
  readonly statusLabel = computed(() => STATUS_LABELS[this.status()] ?? this.status());

  /** US13 AC3 / US15 AC5: changes are possible only while Open and before the cut-off. */
  readonly canEdit = computed(() => this.isOpen() && !this.meta().isLocked);
  readonly isCompleted = computed(() => this.meta().reservationStatus === 'Completed');

  readonly guestsLabel = computed(() => {
    const max = this.invite()?.MaxGuestCount;
    const joined = this.participants().length;
    return max ? `${joined} of ${max} guests` : `${joined} ${joined === 1 ? 'guest' : 'guests'}`;
  });

  /** US20 AC4: "4 of 5 joined · 3 chose dishes". */
  readonly progress = computed(() =>
    progressLabel(this.participants(), this.meta().maxGuests ?? this.invite()?.MaxGuestCount),
  );

  /** US14 AC1: guests who have not chosen any dishes yet. */
  readonly notChosen = computed(() => guestsWithoutDishes(this.participants()));

  /** Link guests open to join the group (same as the one shown after booking). */
  readonly inviteLink = computed(() => {
    const token = this.invite()?.inviteToken;
    return token ? `${window.location.origin}/join/${encodeURIComponent(token)}` : null;
  });

  readonly hasAnyDishes = computed(() => this.participants().some((person) => person.items.length > 0));

  readonly pendingCount = computed(() => this.participants().filter((person) => !person.isSubmitted).length);
  readonly pendingMessage = computed(() => {
    const count = this.pendingCount();
    return count === 1
      ? '1 participant hasn’t submitted their cart yet — their dishes won’t be included in the order'
      : `${count} participants haven’t submitted their cart yet — their dishes won’t be included in the order`;
  });

  readonly canSubmit = computed(
    () => this.canEdit() && this.consolidated().items.length > 0 && !this.submitting(),
  );

  /** US21 AC1: summary of a completed reservation. */
  readonly completedSummary = computed(() => ({
    people: this.participants().length,
    dishes: this.participants().reduce((sum, person) => sum + person.items.reduce((count, item) => count + item.quantity, 0), 0),
    total: this.participants().reduce((sum, person) => sum + person.total, 0),
  }));

  private loadSubscription?: Subscription;
  private syncSubscription?: Subscription;

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.loadSubscription?.unsubscribe();
      this.stopSync();
    });
    this.load();
  }

  // ---- loading

  load(): void {
    this.loadSubscription?.unsubscribe();
    this.stopSync();
    this.loadState.set('loading');

    this.loadSubscription = this.loadSnapshot$().subscribe({
      next: (snapshot) => {
        this.invite.set(snapshot.invite);
        this.ownerSessionId.set(snapshot.ownerSessionId);
        this.participants.set(snapshot.participants);
        this.status.set(snapshot.status);
        this.applyMeta(snapshot.sessions);
        this.markUpdated();
        this.loadState.set('ready');
        this.startSync();
      },
      error: (error: unknown) => {
        this.loadState.set(error instanceof HttpErrorResponse && error.status === 404 ? 'not-found' : 'error');
      },
    });
  }

  private loadSnapshot$(): Observable<GroupOrderSnapshot> {
    return this.groupOrders.getGroupInvite$(this.reservationId).pipe(
      switchMap((invite) =>
        // Owner session id first: the backend creates it if missing, so it is already in the list.
        this.groupOrders.getOwnerSessionId$(this.reservationId).pipe(
          catchError(() => of('')),
          switchMap((ownerSessionId) =>
            forkJoin({
              sessions: this.groupOrders.getSessions$(invite.GroupReservationId),
              status: this.groupOrders.getStatus$(invite.GroupReservationId),
            }).pipe(
              map(({ sessions, status }) => ({
                invite,
                ownerSessionId,
                participants: sessions.participants,
                status: status.status,
                sessions,
              })),
            ),
          ),
        ),
      ),
    );
  }

  // ---- background sync (level 3, US20)

  private startSync(): void {
    this.stopSync();
    if (!this.isOpen()) {
      return;
    }

    this.syncSubscription = interval(this.syncInterval)
      .pipe(
        takeWhile(() => this.isOpen()),
        // If the previous request (or a manual Refresh) is still pending, a new one does not start.
        exhaustMap(() => (this.refreshing() ? EMPTY : this.syncOnce$())),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
  }

  private stopSync(): void {
    this.syncSubscription?.unsubscribe();
    this.syncSubscription = undefined;
  }

  refresh(): void {
    if (this.refreshing() || this.loadState() !== 'ready') {
      return;
    }
    this.syncOnce$().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
  }

  /** Silent refresh: no loader and no toast; on error the last data stays on screen. */
  private syncOnce$(): Observable<GroupOrderSync> {
    const groupId = this.invite()?.GroupReservationId;
    if (!groupId) {
      return EMPTY;
    }

    return defer(() => {
      this.refreshing.set(true);
      return forkJoin({
        sessions: this.groupOrders.getSessions$(groupId, { silent: true }),
        status: this.groupOrders.getStatus$(groupId, { silent: true }),
      });
    }).pipe(
      map(({ sessions, status }) => ({ participants: sessions.participants, status: status.status, sessions })),
      tap((update) => {
        this.announceNewItems(update.participants);
        this.participants.set(update.participants);
        this.status.set(update.status);
        this.applyMeta(update.sessions);
        this.markUpdated();
        if (update.status !== 'Open') {
          this.stopSync();
        }
      }),
      catchError(() => {
        this.refreshFailed.set(true);
        return EMPTY;
      }),
      finalize(() => this.refreshing.set(false)),
    );
  }

  /** US20 AC2: highlight new guest dishes for 10 s and show "Anna added Quinoa Bowl". */
  private announceNewItems(next: Participant[]): void {
    const guestsOnly = next.filter((person) => person.type === 'Guest');
    const added = findNewItems(this.participants(), guestsOnly);
    if (added.length === 0) {
      return;
    }

    const until = Date.now() + NEW_HIGHLIGHT_MS;
    this.highlighted.update((lines) => [
      ...lines.filter((line) => line.until > Date.now() && !added.some((item) => item.key === line.key)),
      ...added.map((item) => ({ key: item.key, until })),
    ]);
    for (const item of added.slice(0, 3)) {
      this.toast.info(`${item.participantName} added ${item.dishName}`);
    }
  }

  /** Keys still inside their 10-second highlight window. */
  readonly newKeys = computed<ReadonlySet<string>>(() => {
    const now = this.clock();
    return new Set(this.highlighted().filter((line) => line.until > now).map((line) => line.key));
  });

  /** US13 AC1: "in 1h 20m" — counted from the server value; refreshed with every sync (5 s). */
  readonly timeLeft = computed(() => {
    const meta = this.meta();
    const elapsed = Math.floor(Math.max(0, this.clock() - meta.receivedAt) / 1000);
    return formatTimeLeft(meta.secondsUntilCutoff - elapsed);
  });

  removedFor(participantId: string): RemovedLine[] {
    return this.removedLines().filter((line) => line.participantId === participantId);
  }

  private applyMeta(sessions: GroupSessionsResponse): void {
    this.meta.set({
      cutoffAt: sessions.cutoffAt ?? null,
      secondsUntilCutoff: sessions.secondsUntilCutoff ?? 0,
      receivedAt: Date.now(),
      isLocked: sessions.isLocked ?? false,
      submittedAt: sessions.submittedAt ?? null,
      submittedAutomatically: sessions.submittedAutomatically ?? false,
      autoRemind: sessions.autoRemind ?? true,
      paymentMode: sessions.paymentMode ?? null,
      maxGuests: sessions.maxGuests ?? null,
      reservationStatus: sessions.reservationStatus ?? null,
    });
    if (sessions.groupStatus) {
      this.status.set(sessions.groupStatus);
    }
  }

  /** A response of an owner action is the fresh group snapshot. */
  private applySnapshot(sessions: GroupSessionsResponse): void {
    this.participants.set(sessions.participants);
    this.applyMeta(sessions);
    this.markUpdated();
  }

  private markUpdated(): void {
    this.clock.set(Date.now());
    this.lastUpdated.set(new Date());
    this.refreshFailed.set(false);
  }

  private groupId(): string | null {
    return this.invite()?.GroupReservationId ?? null;
  }

  /** Runs an owner action; errors are already shown as a toast by tokenInterceptor. */
  private run<T>(request$: Observable<T>, onSuccess: (result: T) => void): void {
    this.busy.set(true);
    request$
      .pipe(
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({ next: onSuccess, error: () => undefined });
  }

  setView(view: GroupOrderView): void {
    this.view.set(view);
  }

  // ---- invite link

  async copyInviteLink(): Promise<void> {
    const link = this.inviteLink();
    if (!link) {
      return;
    }

    try {
      await navigator.clipboard.writeText(link);
      this.toast.success('Share it with your guests.', 'Invite link copied');
    } catch {
      this.toast.error('Could not copy the link. Please copy it manually.');
    }
  }

  // ---- US15: owner edits a guest's order

  removeDish({ participant, item }: DishRemoval): void {
    const groupId = this.groupId();
    if (!groupId || !this.canEdit()) {
      return;
    }

    this.run(this.groupOrders.removeDish$(groupId, participant.id, item.dish.id), (snapshot) => {
      this.applySnapshot(snapshot);
      this.removedLines.update((lines) => [
        ...lines.filter((line) => !(line.participantId === participant.id && line.dishId === item.dish.id)),
        {
          participantId: participant.id,
          participantName: participant.name,
          dishId: item.dish.id,
          dishName: item.dish.name,
          quantity: item.quantity,
          note: item.note,
          serveTime: item.serveTime,
        },
      ]);
    });
  }

  undoRemove(line: RemovedLine): void {
    const groupId = this.groupId();
    if (!groupId) {
      return;
    }

    this.run(
      this.groupOrders.restoreDish$(groupId, line.participantId, line.dishId, {
        quantity: line.quantity,
        note: line.note,
        serveTime: line.serveTime,
      }),
      (snapshot) => {
        this.applySnapshot(snapshot);
        this.removedLines.update((lines) => lines.filter((item) => item !== line));
      },
    );
  }

  askChange(participant: Participant): void {
    const groupId = this.groupId();
    if (!groupId) {
      return;
    }

    this.run(this.groupOrders.requestChange$(groupId, participant.id), (snapshot) => {
      this.applySnapshot(snapshot);
      this.toast.success(`${participant.name} can change the order now`, 'Request sent');
    });
  }

  // ---- US14: reminders

  remind(participant: Participant): void {
    this.sendReminders([participant.id]);
  }

  remindAll(): void {
    this.sendReminders([]);
  }

  private sendReminders(ids: string[]): void {
    const groupId = this.groupId();
    if (!groupId) {
      return;
    }

    this.run(this.groupOrders.remind$(groupId, ids), (result) => {
      this.showReminderResult(result);
      this.refresh();
    });
  }

  private showReminderResult(result: RemindersResponse): void {
    if (result.reminded.length > 0) {
      this.toast.success(`Reminder sent to ${result.reminded.map((person) => person.name).join(', ')}`);
    }
    for (const skipped of result.skipped) {
      const time = skipped.lastRemindedAt ? new Date(skipped.lastRemindedAt) : null;
      const at = time ? time.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : 'recently';
      this.toast.info(`${skipped.name}: already reminded at ${at}`);
    }
    if (result.reminded.length === 0 && result.skipped.length === 0) {
      this.toast.info('Nobody needs a reminder right now');
    }
  }

  toggleAutoRemind(event: Event): void {
    const groupId = this.groupId();
    if (!groupId) {
      return;
    }
    const enabled = (event.target as HTMLInputElement).checked;
    this.run(this.groupOrders.updateSettings$(groupId, enabled), (snapshot) => this.applySnapshot(snapshot));
  }

  // ---- US18/US19: the owner's own dishes

  changeMyDish(change: DishDetailsChange): void {
    const groupId = this.groupId();
    if (!groupId) {
      return;
    }
    this.run(this.groupOrders.updateMyDish$(groupId, change.dishId, change.details), (snapshot) => this.applySnapshot(snapshot));
  }

  changeMyAllergies(allergies: string): void {
    const groupId = this.groupId();
    if (!groupId) {
      return;
    }
    this.run(this.groupOrders.updateMyAllergies$(groupId, allergies), (snapshot) => this.applySnapshot(snapshot));
  }

  // ---- Submit

  openSubmitDialog(): void {
    if (this.canSubmit()) {
      this.submitDialogOpen.set(true);
    }
  }

  closeSubmitDialog(): void {
    if (!this.submitting()) {
      this.submitDialogOpen.set(false);
    }
  }

  confirmSubmit(): void {
    const groupId = this.invite()?.GroupReservationId;
    if (!groupId || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.groupOrders
      .submit$(groupId, this.reservationId)
      .pipe(
        finalize(() => this.submitting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.status.set('Submitted');
          this.meta.update((meta) => ({ ...meta, isLocked: true, submittedAt: new Date().toISOString() }));
          this.removedLines.set([]);
          this.stopSync();
          this.submitDialogOpen.set(false);
          this.toast.success('Pre-order sent to the restaurant');
        },
        // tokenInterceptor has already shown the error toast — just return the screen to a usable state.
        error: () => this.submitDialogOpen.set(false),
      });
  }

  // ---- Remove Guest (bonus, US15 AC4)

  askRemove(participant: Participant): void {
    if (this.canEdit() && participant.type === 'Guest') {
      this.removeTarget.set(participant);
    }
  }

  closeRemoveDialog(): void {
    if (!this.removing()) {
      this.removeTarget.set(null);
    }
  }

  confirmRemove(): void {
    const groupId = this.invite()?.GroupReservationId;
    const target = this.removeTarget();
    if (!groupId || !target || this.removing()) {
      return;
    }

    this.removing.set(true);
    this.groupOrders
      .removeSession$(groupId, target.id)
      .pipe(
        finalize(() => this.removing.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (snapshot) => {
          this.participants.set(snapshot.participants);
          this.markUpdated();
          this.removeTarget.set(null);
          this.removedLines.update((lines) => lines.filter((line) => line.participantId !== target.id));
          this.toast.success(`${target.name} was removed from the reservation`);
        },
        error: () => this.removeTarget.set(null),
      });
  }

  // ---- US17: split the bill (simulated)

  togglePayment(): void {
    const groupId = this.groupId();
    if (!groupId) {
      return;
    }
    if (this.paymentOpen()) {
      this.paymentOpen.set(false);
      return;
    }
    this.paymentOpen.set(true);
    this.runPayment(this.groupOrders.getPayment$(groupId));
  }

  choosePaymentMode(mode: PaymentMode): void {
    const groupId = this.groupId();
    if (groupId) {
      this.runPayment(this.groupOrders.setPaymentMode$(groupId, mode));
    }
  }

  sendPaymentLinks(): void {
    const groupId = this.groupId();
    if (groupId) {
      this.runPayment(this.groupOrders.sendPaymentLinks$(groupId), 'Payment links sent');
    }
  }

  markPaid(person: PaymentParticipant): void {
    const groupId = this.groupId();
    if (groupId && person.id) {
      this.runPayment(this.groupOrders.markPaid$(groupId, person.id));
    }
  }

  remindUnpaid(): void {
    const groupId = this.groupId();
    if (!groupId) {
      return;
    }
    this.run(this.groupOrders.remind$(groupId, []), (result) => this.showReminderResult(result));
  }

  private runPayment(request$: Observable<PaymentView>, successMessage?: string): void {
    this.paymentBusy.set(true);
    request$
      .pipe(
        finalize(() => this.paymentBusy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (view) => {
          this.payment.set(view);
          if (successMessage) {
            this.toast.success(successMessage);
          }
        },
        error: () => undefined,
      });
  }

  // ---- US21: book again with the same group

  bookAgain(): void {
    const invite = this.invite();
    void this.router.navigate(['/tables'], {
      queryParams: {
        locationId: invite?.LocationId,
        guests: invite?.MaxGuestCount,
        rebookFrom: this.reservationId,
      },
    });
  }
}
