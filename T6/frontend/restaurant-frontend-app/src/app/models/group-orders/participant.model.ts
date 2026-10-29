export type ParticipantType = 'Owner' | 'Guest';

/** US19: when a dish is served. */
export type ServeTime = 'OnArrival' | 'AfterMains';

export interface DishShort {
  id: string;
  name: string;
  price: number;
  imageUrl?: string;
  weight?: string;
  /** "Appetizer" | "Main course" | "Dessert" — desserts are served after mains by default (US19). */
  dishType?: string;
}

export interface ParticipantItem {
  dish: DishShort;
  quantity: number;
  /** US18: note for the kitchen, up to 80 characters. */
  note?: string | null;
  /** US19: effective serving time (the backend fills in the default). */
  serveTime?: ServeTime;
}

export interface Participant {
  id: string;
  type: ParticipantType;
  name: string;
  items: ParticipantItem[];
  total: number;
  isSubmitted: boolean;
  /** US18: allergies or dietary needs — a red chip next to the name. */
  allergies?: string | null;
  /** US15: the owner changed this guest's order ("Changed by you"). */
  changedByOwner?: boolean;
  /** US14: last reminder sent to this guest. */
  lastRemindedAt?: string | null;
  /** US17: the participant's share is paid. */
  isPaid?: boolean;
}
