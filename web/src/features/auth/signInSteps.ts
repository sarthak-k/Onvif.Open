import { defineMachine } from '../../shared/stateMachine';

/** The sign-in page: the password, then (two-factor sign-in on) the app's code; or the work email for single sign-on. */
export type SignInStepName = 'password' | 'code' | 'singleSignOn';

/** docs/STATE-MACHINES.md; leaving the page (for the app, or the identity provider) ends it. */
export const signInSteps = defineMachine<SignInStepName>('Sign-in steps', 'password', [
  { from: 'password', to: 'code', when: 'the password was right; two-factor sign-in is on' },
  { from: 'code', to: 'password', when: 'Start over, or the challenge expired' },
  { from: 'password', to: 'singleSignOn', when: 'Use single sign-on, or back from the provider with a problem' },
  { from: 'singleSignOn', to: 'password', when: 'Use a password' },
]);
