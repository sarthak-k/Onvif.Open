import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { useEventsBetween } from '../../data/recordings';
import type { Span } from '../playback/spans';
import { Timeline } from '../playback/Timeline';
import { formatClock, formatDuration, zoneName } from '../playback/time';
import { CameraTile, type SeekCommand } from './CameraTile';

export interface PlayerCamera {
  cameraId: string;
  name: string;
}

/** The marked stretch: in and out points (ms since the epoch). */
export interface ClipMarks {
  from: number | null;
  to: number | null;
}

interface Props {
  siteId: string | null;
  /** The time window shown on the timeline. */
  from: number;
  to: number;
  /** Where playback starts (and "Back to the start" goes); changing it starts over. */
  start: number;
  /** What `start` is, for the button: "the event", "the clip". */
  startLabel: string;
  /** The first camera leads; at most four. */
  cameras: readonly PlayerCamera[];
  /** Marks to start with (a saved clip). */
  initialMarks?: ClipMarks;
  /** Mark in/out buttons and what to do with the marked clip (e.g. save it to a case); none: no marking. */
  clipActions?(marks: { from: number; to: number }): ReactNode;
  /** The site's time zone: every time is shown in it. */
  timeZone?: string;
  footer?: ReactNode;
}

const rates = [0.5, 1, 2, 4, 8];

/**
 * Cameras played in sync over a time window: one clock (the first camera leads), one timeline with what each recorded
 * and the site's events, shared play, pause, speed and jumps, and in/out marks for a clip.
 */
export function SyncedPlayer({ siteId, from, to, start, startLabel, cameras, initialMarks, clipActions, timeZone, footer }: Props) {
  const [seek, setSeek] = useState<SeekCommand>(() => ({ time: start, n: 0 }));
  const [playing, setPlaying] = useState(true);
  const [rate, setRate] = useState(1);
  const [position, setPosition] = useState<number | null>(null);
  const [spans, setSpans] = useState<Record<string, Span[]>>({});
  const [marks, setMarks] = useState<ClipMarks>(initialMarks ?? { from: null, to: null });
  const events = useEventsBetween(siteId, from, to);

  // Somewhere else: start over there.
  useEffect(() => {
    setSeek((s) => ({ time: start, n: s.n + 1 }));
    setPlaying(true);
    setPosition(null);
    setMarks(initialMarks ?? { from: null, to: null });
  }, [start]); // eslint-disable-line react-hooks/exhaustive-deps

  const goTo = useCallback((time: number) => setSeek((s) => ({ time: Math.min(Math.max(time, from), to), n: s.n + 1 })), [from, to]);
  const onSpans = useCallback((cameraId: string, value: Span[]) => setSpans((all) => (all[cameraId] === value ? all : { ...all, [cameraId]: value })), []);
  const shown = new Set(cameras.map((c) => c.cameraId));
  const timelineEvents = (events.data ?? []).filter((e) => e.cameraId && shown.has(e.cameraId));
  const now = position ?? seek.time;
  const marked = marks.from !== null && marks.to !== null && marks.to > marks.from ? { from: marks.from, to: marks.to } : null;

  return (
    <section className="stack" aria-label="Playback">
      <div className={`playback-grid count-${Math.min(cameras.length, 4)}`}>
        {cameras.map((camera, index) => (
          <CameraTile
            key={camera.cameraId}
            cameraId={camera.cameraId}
            name={camera.name}
            from={from}
            to={to}
            seek={seek}
            playing={playing}
            rate={rate}
            lead={index === 0}
            leadPosition={position}
            onPosition={index === 0 ? setPosition : undefined}
            onSpans={onSpans}
          />
        ))}
      </div>

      <div className="toolbar playback-controls">
        <button type="button" onClick={() => goTo(now - 10_000)} aria-label="Back 10 seconds">
          −10 s
        </button>
        <button type="button" className="primary" onClick={() => setPlaying((p) => !p)}>
          {playing ? 'Pause' : 'Play'}
        </button>
        <button type="button" onClick={() => goTo(now + 10_000)} aria-label="Forward 10 seconds">
          +10 s
        </button>
        <label>
          Speed
          <select value={rate} onChange={(e) => setRate(Number(e.target.value))}>
            {rates.map((r) => (
              <option key={r} value={r}>
                {r}×
              </option>
            ))}
          </select>
        </label>
        <button type="button" onClick={() => goTo(start)}>
          Back to {startLabel}
        </button>
        <span className="playback-clock" aria-live="polite">
          {formatClock(now, true, timeZone)} {zoneName(now, timeZone)}
        </span>
      </div>

      <Timeline
        start={from}
        end={to}
        rows={cameras.map((c) => ({ id: c.cameraId, label: c.name, spans: spans[c.cameraId] ?? [] }))}
        events={timelineEvents}
        position={position}
        clip={marks}
        timeZone={timeZone}
        onSeek={goTo}
      />

      {clipActions && (
        <div className="toolbar clip-marks" aria-label="Clip">
          <button type="button" onClick={() => setMarks((m) => ({ from: now, to: m.to !== null && m.to > now ? m.to : null }))}>
            Mark in
          </button>
          <button type="button" onClick={() => setMarks((m) => ({ from: m.from !== null && m.from < now ? m.from : null, to: now }))}>
            Mark out
          </button>
          <span className="muted small">
            {marked
              ? `Clip ${formatClock(marked.from, true, timeZone)} – ${formatClock(marked.to, true, timeZone)} (${formatDuration(marked.to - marked.from)})`
              : marks.from !== null
                ? `In at ${formatClock(marks.from, true, timeZone)}: now mark the end`
                : 'Mark where the clip starts and ends.'}
          </span>
          {marked && clipActions(marked)}
        </div>
      )}
      {footer}
    </section>
  );
}
