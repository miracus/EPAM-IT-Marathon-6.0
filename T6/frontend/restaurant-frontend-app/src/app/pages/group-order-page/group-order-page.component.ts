import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
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
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { consolidatePreOrder } from '@app/shared/helpers/consolidate-pre-order';
import { ParticipantCardComponent } from '@app/components/group-orders/participant-card/participant-card.component';
import { ModalComponent } from '@app/shared/custom-components/modal/modal.component';
import { GroupOrderSnapshot, GroupOrderSync, LoadState } from './models';

/** Як часто підтягувати свіжі кошики, поки група Open. */
export const GROUP_ORDER_SYNC_MS = 15_000;

const STATUS_LABELS: Record<GroupReservationStatus, string> = {
  Open: 'Collecting pre-orders',
  Submitted: 'Sent to Restaurant',
  Closed: 'Closed',
};

@Component({
  selector: 'app-group-order-page',
  imports: [CurrencyPipe, DatePipe, RouterLink, ParticipantCardComponent, ModalComponent],
  templateUrl: './group-order-page.component.html',
  styleUrl: './group-order-page.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class GroupOrderPageComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly groupOrders = inject(GroupOrdersService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly reservationId = this.route.snapshot.paramMap.get('reservationId') ?? '';

  // ---- стан
  readonly loadState = signal<LoadState>('loading');
  readonly invite = signal<GuestInvite | null>(null);
  readonly ownerSessionId = signal('');
  readonly participants = signal<Participant[]>([]);
  readonly status = signal<GroupReservationStatus>('Open');

  readonly lastUpdated = signal<Date | null>(null);
  readonly refreshFailed = signal(false);
  readonly refreshing = signal(false);

  readonly submitDialogOpen = signal(false);
  readonly submitting = signal(false);

  readonly removeTarget = signal<Participant | null>(null);
  readonly removing = signal(false);

  // ---- похідні значення
  /** Замовлення для ресторану: лише підтверджені кошики, однакові страви — одним рядком. */
  readonly consolidated = computed(() => consolidatePreOrder(this.participants()));
  readonly grandTotal = computed(() => this.consolidated().total);
  readonly dishCount = computed(() => this.consolidated().items.reduce((sum, item) => sum + item.quantity, 0));

  readonly isOpen = computed(() => this.status() === 'Open');
  readonly statusLabel = computed(() => STATUS_LABELS[this.status()] ?? this.status());

  readonly guestsLabel = computed(() => {
    const max = this.invite()?.MaxGuestCount;
    const joined = this.participants().length;
    return max ? `${joined} of ${max} guests` : `${joined} ${joined === 1 ? 'guest' : 'guests'}`;
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
    () => this.isOpen() && this.consolidated().items.length > 0 && !this.submitting(),
  );

  private loadSubscription?: Subscription;
  private syncSubscription?: Subscription;

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.loadSubscription?.unsubscribe();
      this.stopSync();
    });
    this.load();
  }

  // ---- завантаження

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
        // Спершу id власної сесії: бекенд створює її, якщо ще немає, — тоді вона вже буде в списку.
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
              })),
            ),
          ),
        ),
      ),
    );
  }

  // ---- фонова синхронізація (рівень 3)

  private startSync(): void {
    this.stopSync();
    if (!this.isOpen()) {
      return;
    }

    this.syncSubscription = interval(GROUP_ORDER_SYNC_MS)
      .pipe(
        takeWhile(() => this.isOpen()),
        // Якщо попередній запит (або ручний Refresh) ще не відповів — новий не стартує.
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

  /** Тихе оновлення: без лоадера і без toast; при помилці лишаємо останні дані. */
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
      map(({ sessions, status }) => ({ participants: sessions.participants, status: status.status })),
      tap((update) => {
        this.participants.set(update.participants);
        this.status.set(update.status);
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

  private markUpdated(): void {
    this.lastUpdated.set(new Date());
    this.refreshFailed.set(false);
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
          this.stopSync();
          this.submitDialogOpen.set(false);
          this.toast.success('Pre-order sent to the restaurant');
        },
        // Toast з текстом помилки вже показав tokenInterceptor — тут лише повертаємо екран у робочий стан.
        error: () => this.submitDialogOpen.set(false),
      });
  }

  // ---- Remove Guest (бонус)

  askRemove(participant: Participant): void {
    if (this.isOpen() && participant.type === 'Guest') {
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
          this.toast.success(`${target.name} was removed from the reservation`);
        },
        error: () => this.removeTarget.set(null),
      });
  }
}
