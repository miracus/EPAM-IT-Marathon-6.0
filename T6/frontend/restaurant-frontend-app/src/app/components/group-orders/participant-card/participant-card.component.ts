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
  /** The Owner's own cart — titled "My Pre-Order". */
  readonly isMe = input(false);
  /** Show "Remove Guest" (guests only, and only while the group is Open). */
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
