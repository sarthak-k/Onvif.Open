import type { LivePlayer, PlayerSource, PlayerState } from './types';

/** How far behind the newest frame the player may fall before it jumps to the live edge. */
export const maxLagSeconds = 1;
/** Where a jump puts the playhead: a little behind the newest frame, so network jitter does not stall it. */
export const edgeOffsetSeconds = 0.3;
/** Video kept behind the playhead (older video is removed: many tiles share the browser's memory). */
const keepBehindSeconds = 10;
const retryDelayMs = 3000;
const codecHeader = 'X-Video-Codec';

/** The recorder said this camera needs another player (not H.264), or this browser cannot play it. */
export class NeedsHls extends Error {}

/** Where to put the playhead: null to leave it, or a time just before the newest frame. */
export function liveEdgeTarget(bufferedEnd: number | null, currentTime: number, bufferedStart: number | null): number | null {
  if (bufferedEnd === null || bufferedStart === null) return null;
  if (currentTime < bufferedStart || bufferedEnd - currentTime > maxLagSeconds) return Math.max(bufferedStart, bufferedEnd - edgeOffsetSeconds);
  return null;
}

/**
 * The playback speed that keeps a small cushion of video ahead of the playhead: a hair slower when frames arrive
 * just in time (so it does not stall at every frame), a hair faster when it drifts behind.
 */
export function rateFor(aheadSeconds: number): number {
  return aheadSeconds < 0.15 ? 0.95 : aheadSeconds > 0.6 ? 1.05 : 1;
}

function describe(status: number): string {
  if (status === 503) return 'No video from the camera yet.';
  if (status === 404) return 'The camera is not recording.';
  if (status === 401 || status === 403) return 'Not allowed to watch this camera.';
  return 'The video could not be played.';
}

/**
 * Low-latency live view: one long fetch of fragmented MP4 (a fragment per frame) fed to Media Source Extensions,
 * the playhead held at the live edge. The token goes in the Authorization header. A dropped stream reconnects;
 * `onNeedsHls` is called when this camera or browser needs the HLS player instead.
 */
export function createMsePlayer(video: HTMLVideoElement, source: PlayerSource & { lowLatencyUrl: string }, onState: (state: PlayerState) => void, onNeedsHls: () => void): LivePlayer {
  let disposed = false;
  let abort: AbortController | null = null;
  let retry: ReturnType<typeof setTimeout> | undefined;
  let objectUrl: string | null = null;

  // Each state once: a stall and resume does not repeat it.
  let last: PlayerState['status'] | null = null;
  const report = (state: PlayerState) => {
    if (state.status === 'playing' && last === 'playing') return;
    last = state.status;
    onState(state);
  };
  const onPlaying = () => report({ status: 'playing' });
  video.addEventListener('playing', onPlaying);

  const cleanUp = () => {
    abort?.abort();
    abort = null;
    if (objectUrl) {
      video.removeAttribute('src');
      video.load();
      URL.revokeObjectURL(objectUrl);
      objectUrl = null;
    }
  };

  const again = (message: string) => {
    if (disposed) return;
    report({ status: 'error', message });
    cleanUp();
    retry = setTimeout(() => void start(), retryDelayMs);
  };

  async function start() {
    if (disposed) return;
    report({ status: 'connecting' });
    const controller = new AbortController();
    abort = controller;
    try {
      const token = source.token();
      const response = await fetch(source.lowLatencyUrl, { headers: token ? { Authorization: `Bearer ${token}` } : {}, signal: controller.signal });
      if (response.status === 415) throw new NeedsHls();
      if (!response.ok || !response.body) return again(describe(response.status));

      const mime = `video/mp4; codecs="${response.headers.get(codecHeader) ?? 'avc1.42e01e'}"`;
      if (!MediaSource.isTypeSupported(mime)) throw new NeedsHls();
      await play(response.body, mime, controller.signal);
      // The recorder ended the stream (the camera reconnected, or this tile fell behind): start again at once.
      if (!disposed && !controller.signal.aborted) {
        cleanUp();
        void start();
      }
    } catch (error) {
      if (disposed || controller.signal.aborted) return;
      if (error instanceof NeedsHls) {
        cleanUp();
        onNeedsHls();
        return;
      }
      again('Connection to the recorder lost.');
    }
  }

  async function play(body: ReadableStream<Uint8Array>, mime: string, signal: AbortSignal) {
    const mediaSource = new MediaSource();
    objectUrl = URL.createObjectURL(mediaSource);
    video.src = objectUrl;
    await new Promise<void>((resolve) => mediaSource.addEventListener('sourceopen', () => resolve(), { once: true }));
    const buffer = mediaSource.addSourceBuffer(mime);
    buffer.mode = 'segments';

    const queue: Uint8Array[] = [];
    let started = false;
    const pump = () => {
      if (buffer.updating || signal.aborted) return;
      const ranges = buffer.buffered;
      if (ranges.length > 0) {
        const start = ranges.start(0);
        const end = ranges.end(ranges.length - 1);
        const target = liveEdgeTarget(end, video.currentTime, start);
        if (target !== null) video.currentTime = target;
        const rate = rateFor(end - video.currentTime);
        if (video.playbackRate !== rate) video.playbackRate = rate;
        if (video.currentTime - start > keepBehindSeconds) {
          buffer.remove(start, video.currentTime - keepBehindSeconds / 2);
          return;
        }
        if (!started) {
          started = true;
          void video.play().catch(() => undefined);
        }
      }
      const next = queue.shift();
      if (next) buffer.appendBuffer(next as BufferSource);
    };
    buffer.addEventListener('updateend', pump);

    const reader = body.getReader();
    signal.addEventListener('abort', () => void reader.cancel().catch(() => undefined), { once: true });
    for (;;) {
      const { done, value } = await reader.read();
      if (done) return;
      queue.push(value);
      pump();
    }
  }

  void start();

  return {
    destroy() {
      disposed = true;
      clearTimeout(retry);
      video.removeEventListener('playing', onPlaying);
      cleanUp();
    },
  };
}
