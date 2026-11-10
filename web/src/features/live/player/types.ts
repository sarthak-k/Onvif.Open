export type PlayerState = { status: 'connecting' } | { status: 'playing' } | { status: 'error'; message: string };

export interface PlayerSource {
  /** HLS: works everywhere, a few seconds behind. */
  url: string;
  /** Fragmented MP4 for Media Source Extensions: well under 2 s behind (H.264 cameras). */
  lowLatencyUrl?: string;
  /** Read at every request, so a renewed access token is used. */
  token(): string | null;
}

export interface LivePlayer {
  destroy(): void;
}

/** Plays a live stream in a video element (strategy: hls.js, native HLS, or a fake in tests). */
export type PlayerFactory = (video: HTMLVideoElement, source: PlayerSource, onState: (state: PlayerState) => void) => LivePlayer;
