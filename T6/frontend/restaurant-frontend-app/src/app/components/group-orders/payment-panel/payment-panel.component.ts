import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { PaymentMode, PaymentParticipant, PaymentView } from '@app/models/group-orders/group-payment.model';

interface PaymentModeOption {
  mode: PaymentMode;
  title: string;
  hint: string;
}

/** US17: how the group pays and who has paid. Payments are simulated — nothing is charged. */
@Component({
  selector: 'app-payment-panel',
  imports: [CurrencyPipe],
  templateUrl: './payment-panel.component.html',
  styleUrl: './payment-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class PaymentPanelComponent {
  readonly view = input.required<PaymentView>();
  readonly busy = input(false);

  readonly modeChange = output<PaymentMode>();
  readonly sendLinks = output<void>();
  readonly markPaid = output<PaymentParticipant>();
  readonly remindUnpaid = output<void>();

  readonly options: PaymentModeOption[] = [
    { mode: 'EachPaysOwn', title: 'Each pays own', hint: 'Everyone pays for their own dishes' },
    { mode: 'OwnerPaysAll', title: 'I pay for all', hint: 'You cover the whole order' },
    { mode: 'SplitEqually', title: 'Split equally', hint: 'Grand Total divided by everyone; rounding goes to you' },
  ];

  readonly unpaidGuests = computed(() =>
    this.view().participants.filter((person) => person.type === 'Guest' && !person.isPaid).length,
  );

  statusLabel(person: PaymentParticipant): string {
    if (person.amount === 0) {
      return 'Covered by organizer';
    }
    return person.isPaid ? 'Paid' : 'Pending';
  }
}
