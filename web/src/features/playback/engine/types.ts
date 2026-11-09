export type PlaybackStatus = 'idle' | 'loading' | 'playing' | 'paused' | 'ended' | 'error';

export interface PlaybackCallbacks {
  /** The wall-clock time on screen (ms since the epoch), as playback moves. */
  onTime(date: number): void;
  onStatus(status: PlaybackStatus, message?: string): void;
}

/** Plays recordings and seeks by wall-clock time (strategy: hls.js, or a fake in tests). */
export interface PlaybackEngine {
  /** Loads a playlist and starts at `startAt` (wall clock). */
  load(url: string, startAt: number): void;
  /** Seeks within the loaded playlist; false when `date` is outside it (load another playlist then). */
  seek(date: number): boolean;
  play(): void;
  pause(): void;
  setRate(rate: number): void;
  destroy(): void;
}

export type PlaybackEngineFactory = (video: HTMLVideoElement, token: () => string | null, callbacks: PlaybackCallbacks) => PlaybackEngine;
