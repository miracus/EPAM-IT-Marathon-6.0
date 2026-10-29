import { ServeTime } from '@app/models/group-orders/participant.model';

/** US18: the guest changed the note of a dish. */
export interface GuestNoteChange {
  dishId: string;
  note: string;
}

/** US19: the guest changed when a dish is served. */
export interface GuestServeChange {
  dishId: string;
  serveTime: ServeTime;
}
