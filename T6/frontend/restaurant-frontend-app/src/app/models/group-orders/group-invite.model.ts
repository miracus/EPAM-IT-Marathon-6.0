import { GuestInvite } from '@app/models/reservations/guest-invite.model';

/** Invite details for the owner screen plus the token used to build the guest link (/join/{token}). */
export interface GroupInvite extends GuestInvite {
  inviteToken?: string;
}
