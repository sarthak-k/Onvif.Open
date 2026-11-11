import { defineMachine } from '../../shared/stateMachine';

/** Setting up an authenticator app (the user's TwoFactorStates on the recorder: Off, then Pending, then On). */
export type TwoFactorSetupStep = 'start' | 'scan' | 'codes';

/** docs/STATE-MACHINES.md */
export const twoFactorSetupSteps = defineMachine<TwoFactorSetupStep>('Two-factor setup', 'start', [
  { from: 'start', to: 'scan', when: 'Set up: the recorder made a secret' },
  { from: 'scan', to: 'start', when: 'Cancel' },
  { from: 'scan', to: 'codes', when: 'a code from the app was right: two-factor is on' },
]);
