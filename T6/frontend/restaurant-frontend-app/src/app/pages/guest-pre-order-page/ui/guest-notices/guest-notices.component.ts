import { ChangeDetectionStrategy, Component, computed, inject, output } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { ToastService } from '@app/services/toast/toast.service';
import { GuestPreorderStore } from '../../state/guest-preorder.store';

const PAYMENT_MODE_LABELS: Record<string, string> = {
  EachPaysOwn: 'each pays own',
  OwnerPaysAll: 'the organizer pays for all',
  SplitEqually: 'split equally',
};

/** Messages from the organizer on the guest screen: US13 lock, US14 reminders, US16 changes, US17 payment, US21 rebook. */
@Component({
  selector: 'app-guest-notices',
  imports: [CurrencyPipe, DatePipe],
  templateUrl: './guest-notices.component.html',
  styleUrl: './guest-notices.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class GuestNoticesComponent {
  readonly store = inject(GuestPreorderStore);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  /** "Pick another dish": the page closes the cart and shows the menu. */
  readonly pickAnother = output<void>();

  readonly organizer = computed(() => this.store.ownerName() || 'The organizer');

  readonly removedTotals = computed(() => {
    const removed = this.store.removedNotices();
    if (removed.length === 0) return null;
    const first = removed[0];
    const last = removed[removed.length - 1];
    return first.oldTotal != null && last.newTotal != null ? { old: first.oldTotal, new: last.newTotal } : null;
  });

  readonly paymentModeLabel = computed(() => {
    const mode = this.store.payment()?.mode;
    return mode ? PAYMENT_MODE_LABELS[mode] ?? mode : '';
  });

  async acknowledge(): Promise<void> {
    await this.store.acknowledgeNotices();
  }

  async pickAnotherDish(): Promise<void> {
    const reopened = await this.store.reopen();
    if (!reopened) return;
    await this.store.acknowledgeNotices();
    this.pickAnother.emit();
  }

  async pay(): Promise<void> {
    if (await this.store.pay()) {
      this.toast.success('Thank you! Your share is paid (demo, no money was charged).', 'Paid');
    }
  }

  joinNew(token: string): void {
    void this.router.navigate(['/join', token]);
  }
}
