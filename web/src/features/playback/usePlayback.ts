import { useCallback, useEffect, useRef, useState, type RefObject } from 'react';
import { useApi } from '../../data/ApiContext';
import { usePlaybackFactory } from './engine/PlaybackContext';
import type { PlaybackEngine, PlaybackStatus } from './engine/types';
import { nextRecorded, type Span } from './spans';

/** How much recording one playlist covers; playback continues with the next one. */
const windowMs = 2 * 60 * 60 * 1000;

/**
 * Playback of a camera's recordings by wall-clock time: seeks inside the loaded playlist, loads another when the time
 * is outside it, skips gaps, and goes on with the next recording when one ends.
 */
export function usePlayback(video: RefObject<HTMLVideoElement | null>, cameraId: string | undefined, spans: readonly Span[]) {
  const { media } = useApi();
  const factory = usePlaybackFactory();
  const engine = useRef<PlaybackEngine | null>(null);
  const spansRef = useRef(spans);
  spansRef.current = spans;
  const [status, setStatus] = useState<PlaybackStatus>('idle');
  const [message, setMessage] = useState<string | undefined>();
  const [position, setPosition] = useState<number | null>(null);
  const [rate, setRateState] = useState(1);
  const positionRef = useRef<number | null>(null);

  const load = useCallback(
    (time: number) => {
      if (!cameraId || !engine.current) return;
      engine.current.load(media.recordingsUrl(cameraId, new Date(time), new Date(time + windowMs)), time);
      positionRef.current = time;
      setPosition(time);
    },
    [cameraId, media],
  );

  useEffect(() => {
    if (!video.current || !cameraId) return;
    const created = factory(video.current, media.token, {
      onTime: (time) => {
        positionRef.current = time;
        setPosition(time);
      },
      onStatus: (next, text) => {
        setStatus(next);
        setMessage(text);
        // The playlist ran out: carry on with the next recording, if there is one.
        if (next === 'ended' && positionRef.current !== null) {
          const following = nextRecorded(spansRef.current, positionRef.current + 1000);
          if (following !== null) load(following);
        }
      },
    });
    engine.current = created;
    setStatus('idle');
    setPosition(null);
    positionRef.current = null;
    return () => {
      created.destroy();
      engine.current = null;
    };
  }, [cameraId, factory, load, media.token, video]);

  /** Plays from `time`, or from the next recording when nothing was recorded then; false when nothing follows. */
  const seek = useCallback(
    (time: number): boolean => {
      const target = nextRecorded(spansRef.current, time);
      if (target === null || !engine.current) return false;
      if (status === 'idle' || status === 'error' || !engine.current.seek(target)) load(target);
      positionRef.current = target;
      setPosition(target);
      return true;
    },
    [load, status],
  );

  const setRate = useCallback((value: number) => {
    setRateState(value);
    engine.current?.setRate(value);
  }, []);

  const play = useCallback(() => engine.current?.play(), []);
  const pause = useCallback(() => engine.current?.pause(), []);

  return {
    status,
    message,
    position,
    rate,
    seek,
    setRate,
    play,
    pause,
  };
}
