import { useCallback, useEffect, useMemo, useState } from 'react';
import type { StoredEvent } from '../../api/types';
import { useEventsBetween } from '../../data/recordings';
import { categoryLabels, startOf, windowOf } from '../playback/events';
import type { Span } from '../playback/spans';
import { Timeline } from '../playback/Timeline';
import { formatClock } from '../playback/time';
import { CameraTile, type SeekCommand } from './CameraTile';

export interface PlayerCamera {
  cameraId: string;
  name: string;
}

interface Props {
  event: StoredEvent;
  /** The event's camera first, then the others shown with it. */
  cameras: readonly PlayerCamera[];
}

const rates = [0.5, 1, 2, 4, 8];

/** Seconds of video before the event when it is opened. */
const openBeforeMs = 5000;

/**
 * The cameras around an event, played in sync: one clock (the event's camera leads), one timeline with what each
 * recorded and the events of the site, shared play, pause, speed and jumps.
 */
export function SyncedPlayer({ event, cameras }: Props) {
  const { from, to } = useMemo(() => windowOf(event), [event]);
  const [seek, setSeek] = useState<SeekCommand>(() => ({ time: startOf(event) - openBeforeMs, n: 0 }));
  const [playing, setPlaying] = useState(true);
  const [rate, setRate] = useState(1);
  const [position, setPosition] = useState<number | null>(null);
  const [spans, setSpans] = useState<Record<string, Span[]>>({});
  const events = useEventsBetween(event.siteId, from, to);

  // Another event: start over at it.
  useEffect(() => {
    setSeek((s) => ({ time: startOf(event) - openBeforeMs, n: s.n + 1 }));
    setPlaying(true);
    setPosition(null);
  }, [event]);

  const goTo = useCallback((time: number) => setSeek((s) => ({ time: Math.min(Math.max(time, from), to), n: s.n + 1 })), [from, to]);
  const onSpans = useCallback((cameraId: string, value: Span[]) => setSpans((all) => (all[cameraId] === value ? all : { ...all, [cameraId]: value })), []);
  const shown = new Set(cameras.map((c) => c.cameraId));
  const timelineEvents = (events.data ?? []).filter((e) => e.cameraId && shown.has(e.cameraId));
  const now = position ?? seek.time;

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
        <button type="button" onClick={() => goTo(startOf(event) - openBeforeMs)}>
          Back to the event
        </button>
        <span className="playback-clock" aria-live="polite">
          {formatClock(now)}
        </span>
      </div>

      <Timeline
        start={from}
        end={to}
        rows={cameras.map((c) => ({ id: c.cameraId, label: c.name, spans: spans[c.cameraId] ?? [] }))}
        events={timelineEvents}
        position={position}
        onSeek={goTo}
      />
      <p className="muted small">
        {categoryLabels[event.category]} at {formatClock(startOf(event))}: the video starts 5 s before it. Click the timeline or an event mark to move all cameras there.
      </p>
    </section>
  );
}
