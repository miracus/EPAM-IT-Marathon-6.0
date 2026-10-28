import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { GuestCartItem } from '@app/pages/guest-pre-order-page/state/guest-preorder.store';
import { GuestNotice } from '@app/models/group-orders/guest-session.model';
import { ServeTime } from '@app/models/group-orders/participant.model';
import { NOTE_MAX_LENGTH, SERVE_TIME_LABELS } from '@app/shared/helpers/group-pre-order';
import { GuestNoteChange, GuestServeChange } from './models';

@Component({
    standalone: true,
    selector: 'app-guest-cart-drawer',
    templateUrl: './guest-cart-drawer.component.html',
    styleUrl: './guest-cart-drawer.component.scss',
    imports: [CurrencyPipe],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GuestCartDrawerComponent {
    readonly open = input(false);
    readonly items = input<GuestCartItem[]>([]);
    readonly total = input(0);
    readonly busy = input(false);
    readonly error = input<string | null>(null);
    /** US13: after submit or the cut-off nothing can be changed. */
    readonly locked = input(false);
    /** US16 AC2: dishes the organizer removed — struck through until "OK, got it". */
    readonly removed = input<GuestNotice[]>([]);
    /** US18: allergies or dietary needs. */
    readonly allergies = input('');

    readonly closed = output<void>();
    readonly goToMenu = output<void>();
    readonly inc = output<string>();
    readonly dec = output<string>();
    readonly remove = output<string>();
    readonly submitted = output<void>();
    readonly noteChange = output<GuestNoteChange>();
    readonly serveChange = output<GuestServeChange>();
    readonly allergiesChange = output<string>();

    readonly noteMax = NOTE_MAX_LENGTH;
    readonly serveLabels = SERVE_TIME_LABELS;
    readonly serveOptions: ServeTime[] = ['OnArrival', 'AfterMains'];

    readonly editingNoteFor = signal<string | null>(null);
    readonly noteDraft = signal('');
    readonly allergiesDraft = signal<string | null>(null);

    startNote(item: GuestCartItem): void {
        this.noteDraft.set(item.note ?? '');
        this.editingNoteFor.set(item.dishId);
    }

    onNoteInput(event: Event): void {
        this.noteDraft.set((event.target as HTMLInputElement).value.slice(0, NOTE_MAX_LENGTH));
    }

    saveNote(item: GuestCartItem): void {
        this.noteChange.emit({ dishId: item.dishId, note: this.noteDraft().trim() });
        this.editingNoteFor.set(null);
    }

    onServeChange(item: GuestCartItem, event: Event): void {
        this.serveChange.emit({ dishId: item.dishId, serveTime: (event.target as HTMLSelectElement).value as ServeTime });
    }

    onAllergiesInput(event: Event): void {
        this.allergiesDraft.set((event.target as HTMLInputElement).value);
    }

    saveAllergies(): void {
        const value = this.allergiesDraft();
        if (value !== null && value.trim() !== this.allergies()) {
            this.allergiesChange.emit(value.trim());
        }
        this.allergiesDraft.set(null);
    }
}
