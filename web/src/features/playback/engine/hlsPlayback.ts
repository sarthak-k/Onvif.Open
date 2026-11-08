import type Hls from 'hls.js';
import { dateAt, timeAt, type Fragment } from './timeMap';
import type { PlaybackEngineFactory } from './types';

/**
 * Recordings through hls.js: the recorder's playlist carries EXT-X-PROGRAM-DATE-TIME for every segment, which maps
 * video time to the wall clock both ways (gaps between recordings are discontinuities). The token goes in the
 * Authorization header of every request.
 */
export const createHlsPlayback: PlaybackEngineFactory = (video, token, callbacks) => {
  let hls: Hls | null = null;
  let fragments: Fragment[] = [];
  let pendingStart: number | null = null;
  let rate = 1;
  let disposed = false;
  let generation = 0;

  const onTimeUpdate = () => {
    const date = dateAt(fragments, video.currentTime);
    if (date !== null) callbacks.onTime(date);
  };
  const onPlaying = () => callbacks.onStatus('playing');
  const onPause = () => !video.ended && callbacks.onStatus('paused');
  const onEnded = () => callbacks.onStatus('ended');
  const onWaiting = () => callbacks.onStatus('loading');

  video.addEventListener('timeupdate', onTimeUpdate);
  video.addEventListener('playing', onPlaying);
  video.addEventListener('pause', onPause);
  video.addEventListener('ended', onEnded);
  video.addEventListener('waiting', onWaiting);

  function startAt(date: number) {
    const time = timeAt(fragments, date) ?? 0;
    video.currentTime = time;
    video.playbackRate = rate;
    void video.play().catch(() => undefined);
  }

  return {
    load(url, start) {
      const current = ++generation;
      hls?.destroy();
      hls = null;
      fragments = [];
      pendingStart = start;
      callbacks.onStatus('loading');

      void import('hls.js').then(({ default: Hls }) => {
        if (disposed || current !== generation) return;
        if (!Hls.isSupported()) {
          callbacks.onStatus('error', 'This browser cannot play recordings.');
          return;
        }

        const player = new Hls({
          xhrSetup: (xhr) => {
            const value = token();
            if (value) xhr.setRequestHeader('Authorization', `Bearer ${value}`);
          },
          maxBufferLength: 30,
          backBufferLength: 30,
        });
        hls = player;

        player.on(Hls.Events.LEVEL_LOADED, (_event, data) => {
          fragments = data.details.fragments
            .filter((f) => f.programDateTime !== null)
            .map((f) => ({ start: f.start, duration: f.duration, programDateTime: f.programDateTime! }));
          if (pendingStart !== null) {
            startAt(pendingStart);
            pendingStart = null;
          }
        });
        player.on(Hls.Events.ERROR, (_event, data) => {
          if (!data.fatal) return;
          const status = data.response?.code;
          callbacks.onStatus('error', status === 404 ? 'No recordings here.' : status === 401 || status === 403 ? 'Not allowed to watch this camera.' : 'The recordings could not be played.');
          player.destroy();
          hls = null;
        });
        player.loadSource(url);
        player.attachMedia(video);
      }, () => callbacks.onStatus('error', 'The video player could not be loaded.'));
    },
    seek(date) {
      if (pendingStart !== null) {
        pendingStart = date;
        return true;
      }
      const first = fragments[0];
      const last = fragments[fragments.length - 1];
      if (!first || date < first.programDateTime || date >= last.programDateTime + last.duration * 1000) return false;
      const time = timeAt(fragments, date);
      if (time === null) return false;
      video.currentTime = time;
      return true;
    },
    play: () => void video.play().catch(() => undefined),
    pause: () => video.pause(),
    setRate(value) {
      rate = value;
      video.playbackRate = value;
    },
    destroy() {
      disposed = true;
      hls?.destroy();
      video.removeEventListener('timeupdate', onTimeUpdate);
      video.removeEventListener('playing', onPlaying);
      video.removeEventListener('pause', onPause);
      video.removeEventListener('ended', onEnded);
      video.removeEventListener('waiting', onWaiting);
      video.removeAttribute('src');
    },
  };
};
