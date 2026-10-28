import { Participant, ParticipantItem, ServeTime } from '@app/models/group-orders/participant.model';

/** Pure helpers of the group pre-order extensions (US13–US21). */

export const NOTE_MAX_LENGTH = 80;

export const SERVE_TIME_LABELS: Record<ServeTime, string> = {
  OnArrival: 'On arrival',
  AfterMains: 'After mains',
};

/** US19 AC2: desserts are served after mains, everything else on arrival. */
export function defaultServeTime(dishType?: string | null): ServeTime {
  return (dishType ?? '').toLowerCase().startsWith('dessert') ? 'AfterMains' : 'OnArrival';
}

/** US19 AC3: a blue chip is shown only when the serving time differs from the default. */
export function isCustomServeTime(item: ParticipantItem): boolean {
  return !!item.serveTime && item.serveTime !== defaultServeTime(item.dish.dishType);
}

/** US13 AC1: "in 1h 20m", "in 5m", "in less than a minute", or null when the cut-off has passed. */
export function formatTimeLeft(seconds: number): string | null {
  if (!Number.isFinite(seconds) || seconds <= 0) {
    return null;
  }
  const totalMinutes = Math.floor(seconds / 60);
  if (totalMinutes === 0) {
    return 'in less than a minute';
  }
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  if (hours === 0) {
    return `in ${minutes}m`;
  }
  return minutes === 0 ? `in ${hours}h` : `in ${hours}h ${minutes}m`;
}

/** US20 AC4: "4 of 5 joined · 3 chose dishes". */
export function progressLabel(participants: Participant[], maxGuests?: number | null): string {
  const joined = participants.length;
  const chose = participants.filter((person) => person.items.length > 0).length;
  const joinedText = maxGuests ? `${joined} of ${maxGuests} joined` : `${joined} joined`;
  return `${joinedText} · ${chose} chose dishes`;
}

/** US14 AC1: guests who have not chosen any dishes yet. */
export function guestsWithoutDishes(participants: Participant[]): Participant[] {
  return participants.filter((person) => person.type === 'Guest' && person.items.length === 0);
}

export interface NewItem {
  key: string;
  participantId: string;
  participantName: string;
  dishName: string;
}

/** Stable key of one dish line of one participant. */
export function itemKey(participantId: string, dishId: string): string {
  return `${participantId}:${dishId}`;
}

/** US20 AC2: dish lines that are new or whose quantity grew since the previous snapshot. */
export function findNewItems(previous: Participant[], next: Participant[]): NewItem[] {
  const before = new Map<string, number>();
  for (const person of previous) {
    for (const item of person.items) {
      before.set(itemKey(person.id, item.dish.id), item.quantity);
    }
  }

  const result: NewItem[] = [];
  for (const person of next) {
    for (const item of person.items) {
      const key = itemKey(person.id, item.dish.id);
      if (item.quantity > (before.get(key) ?? 0)) {
        result.push({ key, participantId: person.id, participantName: person.name, dishName: item.dish.name });
      }
    }
  }
  return result;
}

export interface KitchenNoteLine {
  author: string;
  note: string;
}

export interface KitchenLine {
  dishId: string;
  name: string;
  quantity: number;
  total: number;
  notes: KitchenNoteLine[];
}

export interface KitchenSection {
  serveTime: ServeTime;
  label: string;
  lines: KitchenLine[];
}

export interface KitchenAllergy {
  name: string;
  allergies: string;
}

export interface KitchenView {
  allergies: KitchenAllergy[];
  sections: KitchenSection[];
}

/**
 * US18 AC4 / US19 AC4 — the "By dish" view the kitchen gets: submitted carts only, dishes grouped
 * into "On arrival" and "After mains", notes under the dish with the author's name, allergies on top.
 */
export function buildKitchenView(participants: Participant[]): KitchenView {
  const submitted = participants.filter((person) => person.isSubmitted);
  const sections = new Map<ServeTime, Map<string, KitchenLine>>([
    ['OnArrival', new Map()],
    ['AfterMains', new Map()],
  ]);

  for (const person of submitted) {
    for (const item of person.items) {
      const serveTime = item.serveTime ?? defaultServeTime(item.dish.dishType);
      const lines = sections.get(serveTime) ?? sections.get('OnArrival')!;
      const line = lines.get(item.dish.id) ?? { dishId: item.dish.id, name: item.dish.name, quantity: 0, total: 0, notes: [] };
      const note = item.note?.trim();
      lines.set(item.dish.id, {
        ...line,
        quantity: line.quantity + item.quantity,
        total: line.total + item.dish.price * item.quantity,
        notes: note ? [...line.notes, { author: person.name, note }] : line.notes,
      });
    }
  }

  return {
    allergies: participants
      .filter((person) => !!person.allergies?.trim())
      .map((person) => ({ name: person.name, allergies: person.allergies!.trim() })),
    sections: [...sections.entries()]
      .map(([serveTime, lines]) => ({ serveTime, label: SERVE_TIME_LABELS[serveTime], lines: [...lines.values()] }))
      .filter((section) => section.lines.length > 0),
  };
}
