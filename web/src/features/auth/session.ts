import type { Me } from '../../api/types';
import { defineMachine } from '../../shared/stateMachine';

/** Where the signed-in session stands (AuthContext holds it as the user: undefined, null or who). */
export type SessionState = 'resuming' | 'signedOut' | 'mustChangePassword' | 'mustSetUpTwoFactor' | 'signedIn';

/** The session (docs/STATE-MACHINES.md); a rejected refresh token signs out from any state. */
export const sessionMachine = defineMachine<SessionState>('Session', 'resuming', [
  { from: 'resuming', to: 'signedIn', when: 'stored session still valid' },
  { from: 'resuming', to: 'mustChangePassword', when: 'stored session, temporary password' },
  { from: 'resuming', to: 'mustSetUpTwoFactor', when: 'stored session; the organization requires two-factor' },
  { from: 'resuming', to: 'signedOut', when: 'no or expired session' },
  { from: 'signedOut', to: 'signedIn', when: 'signed in, or set up the organization' },
  { from: 'signedOut', to: 'mustChangePassword', when: 'signed in with a temporary password' },
  { from: 'signedOut', to: 'mustSetUpTwoFactor', when: 'signed in; the organization requires two-factor' },
  { from: 'mustChangePassword', to: 'signedIn', when: 'password changed' },
  { from: 'mustChangePassword', to: 'mustSetUpTwoFactor', when: 'password changed; two-factor still to set up' },
  { from: 'mustChangePassword', to: 'signedOut', when: 'Sign out, or session rejected' },
  { from: 'mustSetUpTwoFactor', to: 'signedIn', when: 'two-factor sign-in turned on' },
  { from: 'mustSetUpTwoFactor', to: 'signedOut', when: 'Sign out, or session rejected' },
  { from: 'signedIn', to: 'mustSetUpTwoFactor', when: 'the organization started requiring two-factor' },
  { from: 'signedIn', to: 'signedOut', when: 'Sign out, or session rejected' },
]);

export function sessionOf(user: Me | null | undefined): SessionState {
  if (user === undefined) return 'resuming';
  if (user === null) return 'signedOut';
  if (user.mustChangePassword) return 'mustChangePassword';
  return user.mustSetUpTwoFactor ? 'mustSetUpTwoFactor' : 'signedIn';
}
