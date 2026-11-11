import { defineMachine } from '../../shared/stateMachine';

/** The sign-in page: the password, then (two-factor sign-in on) the app's code. */
export type SignInStepName = 'password' | 'code';

/** docs/STATE-MACHINES.md; leaving the page for the app ends it. */
export const signInSteps = defineMachine<SignInStepName>('Sign-in steps', 'password', [
  { from: 'password', to: 'code', when: 'the password was right; two-factor sign-in is on' },
  { from: 'code', to: 'password', when: 'Start over, or the challenge expired' },
]);
