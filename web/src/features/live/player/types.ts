export type PlayerState = { status: 'connecting' } | { status: 'playing' } | { status: 'error'; message: string };

export interface PlayerSource {
  url: string;
  /** Read at every request, so a renewed access token is used. */
  token(): string | null;
}

export interface LivePlayer {
  destroy(): void;
}

/** Plays a live stream in a video element (strategy: hls.js, native HLS, or a fake in tests). */
export type PlayerFactory = (video: HTMLVideoElement, source: PlayerSource, onState: (state: PlayerState) => void) => LivePlayer;
