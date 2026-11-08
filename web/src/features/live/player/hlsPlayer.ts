import type { ErrorData } from 'hls.js';
import type { LivePlayer, PlayerFactory, PlayerSource, PlayerState } from './types';

const retryDelayMs = 5000;

function describe(data: ErrorData): string {
  const status = data.response?.code;
  if (status === 503) return 'No video from the camera yet.';
  if (status === 404) return 'The camera is not recording.';
  if (status === 401 || status === 403) return 'Not allowed to watch this camera.';
  return data.type === 'networkError' ? 'Connection to the recorder lost.' : 'The video could not be played.';
}

/**
 * hls.js (loaded on first use, in its own chunk) where the browser has Media Source Extensions; the token goes in
 * the Authorization header, never in a URL. Safari's native HLS gets it as access_token instead. A failed stream is
 * retried every few seconds until the tile goes away.
 */
export const createHlsPlayer: PlayerFactory = (video, source, onState) => {
  let disposed = false;
  let current: LivePlayer | null = null;
  let retry: ReturnType<typeof setTimeout> | undefined;

  const fail = (message: string) => {
    onState({ status: 'error', message });
    current?.destroy();
    current = null;
    retry = setTimeout(start, retryDelayMs);
  };

  const onPlaying = () => onState({ status: 'playing' });
  video.addEventListener('playing', onPlaying);

  function start() {
    if (disposed) return;
    onState({ status: 'connecting' });

    void import('hls.js').then(({ default: Hls }) => {
      if (disposed) return;

      if (Hls.isSupported()) {
        current = attachHls(Hls, video, source, fail);
      } else if (video.canPlayType('application/vnd.apple.mpegurl')) {
        current = attachNative(video, source, fail);
      } else {
        onState({ status: 'error', message: 'This browser cannot play live video.' });
      }
    }, () => fail('The video player could not be loaded.'));
  }

  start();

  return {
    destroy() {
      disposed = true;
      clearTimeout(retry);
      video.removeEventListener('playing', onPlaying);
      current?.destroy();
    },
  };
};

function attachHls(Hls: typeof import('hls.js').default, video: HTMLVideoElement, source: PlayerSource, fail: (message: string) => void): LivePlayer {
  const hls = new Hls({
    xhrSetup: (xhr) => {
      const token = source.token();
      if (token) xhr.setRequestHeader('Authorization', `Bearer ${token}`);
    },
    // Live: stay near the edge, keep little behind (many tiles share the browser's memory).
    liveSyncDurationCount: 2,
    maxBufferLength: 8,
    backBufferLength: 4,
    enableWorker: true,
  });

  hls.on(Hls.Events.MANIFEST_PARSED, () => void video.play().catch(() => undefined));
  hls.on(Hls.Events.ERROR, (_event, data) => {
    if (data.fatal) fail(describe(data));
  });
  hls.loadSource(source.url);
  hls.attachMedia(video);

  return {
    destroy() {
      hls.destroy();
      video.removeAttribute('src');
    },
  };
}

function attachNative(video: HTMLVideoElement, source: PlayerSource, fail: (message: string) => void): LivePlayer {
  const token = source.token();
  const onError = () => fail('The video could not be played.');
  video.addEventListener('error', onError);
  video.src = token ? `${source.url}${source.url.includes('?') ? '&' : '?'}access_token=${encodeURIComponent(token)}` : source.url;
  void video.play().catch(() => undefined);

  return {
    destroy() {
      video.removeEventListener('error', onError);
      video.removeAttribute('src');
      video.load();
    },
  };
}

export type { PlayerState };
