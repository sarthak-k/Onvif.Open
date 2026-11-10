import { defineMachine } from '../../../shared/stateMachine';

export type Step = 'find' | 'connect' | 'name' | 'done';

/** The add-camera wizard's steps (docs/STATE-MACHINES.md); Done is final: the cameras were added. */
export const wizardSteps = defineMachine<Step>('Add-camera wizard', 'find', [
  { from: 'find', to: 'connect', when: 'cameras chosen' },
  { from: 'connect', to: 'find', when: 'Back' },
  { from: 'connect', to: 'name', when: 'at least one camera ready' },
  { from: 'name', to: 'connect', when: 'Back' },
  { from: 'name', to: 'done', when: 'Add cameras' },
]);
