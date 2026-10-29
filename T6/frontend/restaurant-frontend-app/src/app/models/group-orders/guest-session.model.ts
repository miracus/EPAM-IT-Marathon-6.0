import { ParticipantItem } from './participant.model';
import { PaymentMode } from './group-payment.model';

export type GuestNoticeType = 'DishRemoved' | 'Reminder' | 'ChangeRequested' | 'PaymentRequested';

/** A message for the guest: owner's change (US16), reminder (US14), payment request (US17). */
export interface GuestNotice {
  id: string;
  message: string;
  dishId?: string | null;
  createdAt: string;
  type?: GuestNoticeType | null;
  actorName?: string | null;
  dishName?: string | null;
  quantity?: number | null;
  oldTotal?: number | null;
  newTotal?: number | null;
  amount?: number | null;
}

/** US17: the guest's share once the organizer has chosen how the group pays. */
export interface GuestPaymentShare {
  mode: PaymentMode;
  amount: number;
  isPaid: boolean;
  linkSentAt?: string | null;
}

/** US21: the organizer booked again with the same group. */
export interface GuestRebook {
  inviteToken: string;
  startTime: string;
  endTime: string;
}

/** GET /guest/sessions/{guestSessionId} */
export interface GuestSessionInfo {
  name: string;
  items: ParticipantItem[];
  total: number;
  isSubmitted: boolean;
  notices?: GuestNotice[];
  allergies?: string | null;
  groupStatus?: string | null;
  cutoffAt?: string | null;
  secondsUntilCutoff?: number;
  isLocked?: boolean;
  ownerName?: string | null;
  payment?: GuestPaymentShare | null;
  rebook?: GuestRebook | null;
}
