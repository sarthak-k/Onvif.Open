import { memo, useEffect, useMemo, useRef } from 'react';
import { useSegments } from '../../data/recordings';
import { recordedSpans, type Span } from '../playback/spans';
import { usePlayback } from '../playback/usePlayback';

/** What the group asks every tile to do; `n` changes with every new seek. */
export interface SeekCommand {
  time: number;
  n: number;
}

interface Props {
  cameraId: string;
  name: string;
  from: number;
  to: number;
  seek: SeekCommand | null;
  playing: boolean;
  rate: number;
  /** The first tile sets the time for the others. */
  lead: boolean;
  leadPosition: number | null;
  onPosition?(time: number | null): void;
  onSpans(cameraId: string, spans: Span[]): void;
}

/** Followers further than this from the lead are moved to it. */
const maxDriftMs = 2000;

/** One camera of a synced playback: plays its recordings at the group's time, speed and state. */
export const CameraTile = memo(function CameraTile({ cameraId, name, from, to, seek, playing, rate, lead, leadPosition, onPosition, onSpans }: Props) {
  const video = useRef<HTMLVideoElement>(null);
  const segments = useSegments(cameraId, from, to);
  const spans = useMemo(() => recordedSpans(segments.data ?? []), [segments.data]);
  const playback = usePlayback(video, cameraId, spans);
  const pending = useRef<number | null>(null);
  const { seek: seekTo, play, pause, setRate, position, status, message } = playback;

  useEffect(() => onSpans(cameraId, spans), [cameraId, spans, onSpans]);

  // A seek waits until the recordings are listed.
  useEffect(() => {
    if (seek) pending.current = seek.time;
  }, [seek]);
  useEffect(() => {
    if (pending.current !== null && segments.isSuccess) {
      const time = pending.current;
      pending.current = null;
      seekTo(time);
    }
  }, [seek, segments.isSuccess, spans, seekTo]);

  // The group's play state wins, also over a playlist that starts by itself once loaded.
  useEffect(() => {
    if (playing && status === 'paused') play();
    if (!playing && status === 'playing') pause();
  }, [playing, status, play, pause]);

  useEffect(() => setRate(rate), [rate, setRate]);

  useEffect(() => {
    if (lead) onPosition?.(position);
  }, [lead, position, onPosition]);

  // Followers keep up with the lead camera.
  useEffect(() => {
    if (lead || leadPosition === null || !playing) return;
    if (position === null || Math.abs(position - leadPosition) > maxDriftMs) seekTo(leadPosition);
  }, [lead, leadPosition, playing, position, seekTo]);

  const nothing = segments.isSuccess && spans.length === 0;
  const notice = nothing ? 'Nothing recorded in this time' : status === 'error' ? message : status === 'loading' ? 'Loading…' : null;

  return (
    <figure className="tile playback-tile" aria-label={name}>
      <video ref={video} muted playsInline />
      <figcaption className="tile-bar">
        <span className="tile-name">{name}</span>
      </figcaption>
      {notice && <div className="tile-notice">{notice}</div>}
    </figure>
  );
});
