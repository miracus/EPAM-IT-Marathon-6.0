import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { Participant } from '@app/models/group-orders/participant.model';

@Component({
  selector: 'app-participant-card',
  imports: [CurrencyPipe],
  templateUrl: './participant-card.component.html',
  styleUrl: './participant-card.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class ParticipantCardComponent {
  readonly participant = input.required<Participant>();
  /** Картка власного кошика Owner — підпис «My Pre-Order». */
  readonly isMe = input(false);
  /** Показати «Remove Guest» (лише гості й лише поки група Open). */
  readonly canRemove = input(false);

  readonly removeClick = output<Participant>();

  readonly title = computed(() => {
    const person = this.participant();
    if (this.isMe()) {
      return 'My Pre-Order';
    }
    return person.type === 'Owner' ? `Owner: ${person.name}` : `Guest: ${person.name}`;
  });

  readonly removeLabel = computed(() => `Remove ${this.participant().name}`);

  onRemove(): void {
    this.removeClick.emit(this.participant());
  }
}
