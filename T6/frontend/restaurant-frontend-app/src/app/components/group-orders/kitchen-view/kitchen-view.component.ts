import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { Participant } from '@app/models/group-orders/participant.model';
import { buildKitchenView } from '@app/shared/helpers/group-pre-order';

/** US18 AC4 / US19 AC4 — "By dish": what the kitchen gets, grouped by serving time, with notes and allergies. */
@Component({
  selector: 'app-kitchen-view',
  imports: [CurrencyPipe],
  templateUrl: './kitchen-view.component.html',
  styleUrl: './kitchen-view.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class KitchenViewComponent {
  readonly participants = input.required<Participant[]>();

  readonly view = computed(() => buildKitchenView(this.participants()));
}
