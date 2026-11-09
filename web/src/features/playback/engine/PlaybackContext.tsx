import { createContext, useContext, type ReactNode } from 'react';
import { createHlsPlayback } from './hlsPlayback';
import type { PlaybackEngineFactory } from './types';

const PlaybackContext = createContext<PlaybackEngineFactory>(createHlsPlayback);

/** Replaces how recordings are played (tests use a fake; hls.js by default). */
export function PlaybackProvider({ factory, children }: { factory: PlaybackEngineFactory; children: ReactNode }) {
  return <PlaybackContext.Provider value={factory}>{children}</PlaybackContext.Provider>;
}

export const usePlaybackFactory = (): PlaybackEngineFactory => useContext(PlaybackContext);
