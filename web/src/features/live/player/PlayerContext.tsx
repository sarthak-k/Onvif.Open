import { createContext, useContext, type ReactNode } from 'react';
import { createLivePlayer } from './livePlayer';
import type { PlayerFactory } from './types';

const PlayerContext = createContext<PlayerFactory>(createLivePlayer);

/** Replaces how live video is played (tests use a fake; low-latency MP4, else HLS, by default). */
export function PlayerProvider({ factory, children }: { factory: PlayerFactory; children: ReactNode }) {
  return <PlayerContext.Provider value={factory}>{children}</PlayerContext.Provider>;
}

export const usePlayerFactory = (): PlayerFactory => useContext(PlayerContext);
