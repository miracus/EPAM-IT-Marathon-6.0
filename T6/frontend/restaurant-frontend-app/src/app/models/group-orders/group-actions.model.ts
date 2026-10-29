import { Participant, ParticipantItem, ServeTime } from './participant.model';

/** US14: result of POST /guest/groups/{id}/reminders. */
export interface ReminderResult {
  id: string;
  name: string;
  lastRemindedAt?: string | null;
}

export interface RemindersResponse {
  reminded: ReminderResult[];
  skipped: ReminderResult[];
}

/** US15 Undo: what is needed to put a removed dish back. */
export interface RestoreDishRequest {
  quantity: number;
  note?: string | null;
  serveTime?: ServeTime;
}

/** US18/US19: note and serving time of one dish. */
export interface DishDetailsRequest {
  note?: string;
  serveTime?: ServeTime;
}

/** US21: POST /reservations/{id}/rebook. */
export interface RebookResponse {
  groupReservationId: string;
  inviteToken: string;
  invitedParticipants: number;
}

/** US15 AC2: a dish the owner just removed — shown as "‹dish› removed — Anna will be notified · Undo". */
export interface RemovedLine {
  participantId: string;
  participantName: string;
  dishId: string;
  dishName: string;
  quantity: number;
  note?: string | null;
  serveTime?: ServeTime;
}

/** Owner card events: a dish change and the changed fields. */
export interface DishDetailsChange {
  dishId: string;
  details: DishDetailsRequest;
}

/** Owner removes one dish from a guest card. */
export interface DishRemoval {
  participant: Participant;
  item: ParticipantItem;
}
