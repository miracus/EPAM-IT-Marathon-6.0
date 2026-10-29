import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { Participant, ParticipantItem, ServeTime } from '@app/models/group-orders/participant.model';
import { DishDetailsChange, DishRemoval, RemovedLine } from '@app/models/group-orders/group-actions.model';
import { NOTE_MAX_LENGTH, SERVE_TIME_LABELS, isCustomServeTime, itemKey } from '@app/shared/helpers/group-pre-order';

@Component({
  selector: 'app-participant-card',
  imports: [CurrencyPipe, DatePipe],
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
  /** US15: the owner may change this cart (group Open and before the cut-off). */
  readonly canEdit = input(false);
  /** US13 AC3: after submit or the cut-off items show a lock icon. */
  readonly locked = input(false);
  /** US20 AC2: keys of lines that just appeared ("NEW" for 10 seconds). */
  readonly newKeys = input<ReadonlySet<string>>(new Set());
  /** US15 AC2: dishes of this participant the owner just removed. */
  readonly removedLines = input<RemovedLine[]>([]);

  readonly removeClick = output<Participant>();
  readonly removeDish = output<DishRemoval>();
  readonly undo = output<RemovedLine>();
  readonly remind = output<Participant>();
  readonly askChange = output<Participant>();
  /** US18/US19 on the owner's own card. */
  readonly detailsChange = output<DishDetailsChange>();
  readonly allergiesChange = output<string>();

  readonly noteMax = NOTE_MAX_LENGTH;
  readonly serveLabels = SERVE_TIME_LABELS;
  readonly serveOptions: ServeTime[] = ['OnArrival', 'AfterMains'];

  readonly menuOpen = signal(false);
  readonly editingNoteFor = signal<string | null>(null);
  readonly noteDraft = signal('');
  readonly editingAllergies = signal(false);
  readonly allergiesDraft = signal('');

  readonly title = computed(() => {
    const person = this.participant();
    if (this.isMe()) {
      return 'My Pre-Order';
    }
    return person.type === 'Owner' ? `Owner: ${person.name}` : `Guest: ${person.name}`;
  });

  readonly removeLabel = computed(() => `Remove ${this.participant().name}`);
  readonly isGuest = computed(() => this.participant().type === 'Guest');
  /** The owner edits notes, serving time and allergies of their own pre-order. */
  readonly canEditOwn = computed(() => this.isMe() && this.canEdit());
  /** The owner removes guests' dishes, reminds them, asks them to change the order. */
  readonly canManageGuest = computed(() => this.isGuest() && this.canEdit());

  isNew(item: ParticipantItem): boolean {
    return this.newKeys().has(itemKey(this.participant().id, item.dish.id));
  }

  showServeChip(item: ParticipantItem): boolean {
    return isCustomServeTime(item);
  }

  onRemove(): void {
    this.menuOpen.set(false);
    this.removeClick.emit(this.participant());
  }

  onRemoveDish(item: ParticipantItem): void {
    this.removeDish.emit({ participant: this.participant(), item });
  }

  onRemind(): void {
    this.menuOpen.set(false);
    this.remind.emit(this.participant());
  }

  onAskChange(): void {
    this.menuOpen.set(false);
    this.askChange.emit(this.participant());
  }

  toggleMenu(): void {
    this.menuOpen.update((open) => !open);
  }

  // ---- US18: note on the owner's own dish

  startNote(item: ParticipantItem): void {
    this.noteDraft.set(item.note ?? '');
    this.editingNoteFor.set(item.dish.id);
  }

  onNoteInput(event: Event): void {
    this.noteDraft.set((event.target as HTMLInputElement).value.slice(0, NOTE_MAX_LENGTH));
  }

  saveNote(item: ParticipantItem): void {
    this.detailsChange.emit({ dishId: item.dish.id, details: { note: this.noteDraft().trim() } });
    this.editingNoteFor.set(null);
  }

  cancelNote(): void {
    this.editingNoteFor.set(null);
  }

  // ---- US19: serving time of the owner's own dish

  onServeChange(item: ParticipantItem, event: Event): void {
    const serveTime = (event.target as HTMLSelectElement).value as ServeTime;
    this.detailsChange.emit({ dishId: item.dish.id, details: { serveTime } });
  }

  // ---- US18: the owner's own allergies

  startAllergies(): void {
    this.allergiesDraft.set(this.participant().allergies ?? '');
    this.editingAllergies.set(true);
  }

  onAllergiesInput(event: Event): void {
    this.allergiesDraft.set((event.target as HTMLInputElement).value);
  }

  saveAllergies(): void {
    this.allergiesChange.emit(this.allergiesDraft().trim());
    this.editingAllergies.set(false);
  }
}
