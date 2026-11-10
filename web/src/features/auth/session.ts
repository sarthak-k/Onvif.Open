import type { Me } from '../../api/types';
import { defineMachine } from '../../shared/stateMachine';

/** Where the signed-in session stands (AuthContext holds it as the user: undefined, null or who). */
export type SessionState = 'resuming' | 'signedOut' | 'mustChangePassword' | 'signedIn';

/** The session (docs/STATE-MACHINES.md); a rejected refresh token signs out from any state. */
export const sessionMachine = defineMachine<SessionState>('Session', 'resuming', [
  { from: 'resuming', to: 'signedIn', when: 'stored session still valid' },
  { from: 'resuming', to: 'mustChangePassword', when: 'stored session, temporary password' },
  { from: 'resuming', to: 'signedOut', when: 'no or expired session' },
  { from: 'signedOut', to: 'signedIn', when: 'signed in, or set up the organization' },
  { from: 'signedOut', to: 'mustChangePassword', when: 'signed in with a temporary password' },
  { from: 'mustChangePassword', to: 'signedIn', when: 'password changed' },
  { from: 'mustChangePassword', to: 'signedOut', when: 'Sign out, or session rejected' },
  { from: 'signedIn', to: 'signedOut', when: 'Sign out, or session rejected' },
]);

export function sessionOf(user: Me | null | undefined): SessionState {
  return user === undefined ? 'resuming' : user === null ? 'signedOut' : user.mustChangePassword ? 'mustChangePassword' : 'signedIn';
}
