import { createHlsPlayer } from './hlsPlayer';
import { createMsePlayer } from './msePlayer';
import type { LivePlayer, PlayerFactory } from './types';

/** Whether this browser has Media Source Extensions (iPhones before iOS 17.1 do not). */
const hasMse = () => typeof window !== 'undefined' && 'MediaSource' in window && typeof MediaSource.isTypeSupported === 'function';

/**
 * Live video: the low-latency MP4 stream where the browser and camera allow it (well under 2 s behind), else HLS.
 * Once a tile falls back to HLS it stays there until it is shown again.
 */
export const createLivePlayer: PlayerFactory = (video, source, onState) => {
  const { lowLatencyUrl } = source;
  if (!lowLatencyUrl || !hasMse()) return createHlsPlayer(video, source, onState);

  let current: LivePlayer | null = null;
  let disposed = false;
  current = createMsePlayer(video, { ...source, lowLatencyUrl }, onState, () => {
    current?.destroy();
    if (!disposed) current = createHlsPlayer(video, source, onState);
  });

  return {
    destroy() {
      disposed = true;
      current?.destroy();
    },
  };
};
