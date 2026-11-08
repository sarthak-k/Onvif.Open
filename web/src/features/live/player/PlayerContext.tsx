import { createContext, useContext, type ReactNode } from 'react';
import { createHlsPlayer } from './hlsPlayer';
import type { PlayerFactory } from './types';

const PlayerContext = createContext<PlayerFactory>(createHlsPlayer);

/** Replaces how live video is played (tests use a fake; hls.js by default). */
export function PlayerProvider({ factory, children }: { factory: PlayerFactory; children: ReactNode }) {
  return <PlayerContext.Provider value={factory}>{children}</PlayerContext.Provider>;
}

export const usePlayerFactory = (): PlayerFactory => useContext(PlayerContext);
