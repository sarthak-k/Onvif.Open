import type { PtzVector } from '../../../api/types';
import { defineMachine } from '../../../shared/stateMachine';

export type HoldState = 'idle' | 'moving';

/**
 * Hold to move (docs/STATE-MACHINES.md). Every move stops by itself after `safetyMs`, and is renewed while the button
 * is held: if the browser goes away mid-move, the camera stops on its own instead of turning until someone notices.
 */
export const ptzHoldMachine = defineMachine<HoldState>('PTZ hold to move', 'idle', [
  { from: 'idle', to: 'moving', when: 'a direction is pressed' },
  { from: 'moving', to: 'moving', when: 'still held: renewed every second, or another direction' },
  { from: 'moving', to: 'idle', when: 'released, or the panel closes: stop sent' },
]);

/** Each move stops by itself after this long, unless renewed. */
export const safetyMs = 1500;
/** Moves are renewed this often while held. */
export const renewMs = 1000;

export interface PtzMover {
  move(speed: PtzVector, durationMs: number): Promise<void>;
  stop(): Promise<void>;
}

export interface PtzHold {
  readonly state: HoldState;
  press(speed: PtzVector): void;
  release(): void;
  dispose(): void;
}

export function createPtzHold(mover: PtzMover, onError: (error: unknown) => void, timers: { setInterval: typeof setInterval; clearInterval: typeof clearInterval } = globalThis): PtzHold {
  let state: HoldState = 'idle';
  let renew: ReturnType<typeof setInterval> | undefined;
  let current: PtzVector = {};
  const send = () => mover.move(current, safetyMs).catch(onError);

  return {
    get state() {
      return state;
    },
    press(speed) {
      state = ptzHoldMachine.move(state, 'moving');
      current = speed;
      timers.clearInterval(renew);
      void send();
      renew = timers.setInterval(() => {
        state = ptzHoldMachine.move(state, 'moving');
        void send();
      }, renewMs);
    },
    release() {
      if (state === 'idle') return;
      state = ptzHoldMachine.move(state, 'idle');
      timers.clearInterval(renew);
      renew = undefined;
      void mover.stop().catch(onError);
    },
    dispose() {
      this.release();
    },
  };
}
