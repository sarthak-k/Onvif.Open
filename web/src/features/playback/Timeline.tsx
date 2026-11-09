import { memo, useMemo, type KeyboardEvent, type MouseEvent } from 'react';
import type { StoredEvent } from '../../api/types';
import { categoryLabels, severityClass, startOf } from './events';
import type { Span } from './spans';
import { formatClock } from './time';

export interface TimelineRow {
  id: string;
  label: string;
  spans: readonly Span[];
}

interface Props {
  start: number;
  end: number;
  rows: readonly TimelineRow[];
  events: readonly StoredEvent[];
  position: number | null;
  onSeek(time: number): void;
}

/** Tick spacing for the window length: about 6 to 12 labels. */
function tickStep(duration: number): number {
  const second = 1000;
  const minute = 60 * second;
  const steps = [10 * second, 30 * second, minute, 2 * minute, 5 * minute, 10 * minute, 15 * minute, 30 * minute, 60 * minute, 2 * 60 * minute, 3 * 60 * minute];
  return steps.find((step) => duration / step <= 12) ?? steps[steps.length - 1];
}

/**
 * The time window as one bar per camera with what it recorded, the events as marks above, and the playhead. Click a
 * bar (or use the arrow keys: 10 s, with Shift 1 min) to move all cameras there.
 */
export const Timeline = memo(function Timeline({ start, end, rows, events, position, onSeek }: Props) {
  const duration = Math.max(end - start, 1);
  const clamp = (time: number) => Math.min(Math.max(time, start), end);
  const percent = (time: number) => `${((clamp(time) - start) / duration) * 100}%`;
  const width = (from: number, to: number) => `${((clamp(to) - clamp(from)) / duration) * 100}%`;

  const ticks = useMemo(() => {
    const step = tickStep(duration);
    const result: number[] = [];
    for (let t = Math.ceil(start / step) * step; t <= end; t += step) result.push(t);
    return result;
  }, [start, end, duration]);

  const seekAt = (event: MouseEvent<HTMLDivElement>) => {
    const box = event.currentTarget.getBoundingClientRect();
    onSeek(start + ((event.clientX - box.left) / box.width) * duration);
  };
  const onKey = (event: KeyboardEvent) => {
    const step = event.shiftKey ? 60_000 : 10_000;
    const now = position ?? start;
    const moves: Record<string, number> = { ArrowLeft: now - step, ArrowRight: now + step, Home: start, End: end - 1000 };
    if (event.key in moves) {
      event.preventDefault();
      onSeek(clamp(moves[event.key]));
    }
  };
  const visible = events.filter((e) => startOf(e) >= start && startOf(e) < end);

  return (
    <div
      className="timeline"
      role="slider"
      tabIndex={0}
      aria-label="Time"
      aria-valuemin={start}
      aria-valuemax={end}
      aria-valuenow={position ?? start}
      aria-valuetext={position ? formatClock(position) : 'Not playing'}
      onKeyDown={onKey}
    >
      <div className="timeline-events" onClick={seekAt}>
        {visible.map((e) => (
          <button
            key={e.id}
            type="button"
            className={`timeline-event ${severityClass(e)}`}
            style={{ left: percent(startOf(e)) }}
            title={`${formatClock(startOf(e))} ${categoryLabels[e.category]}: ${e.title}${e.cameraName ? ` (${e.cameraName})` : ''}`}
            aria-label={`${categoryLabels[e.category]} at ${formatClock(startOf(e))}`}
            onClick={(click) => {
              click.stopPropagation();
              onSeek(startOf(e));
            }}
          />
        ))}
      </div>
      {rows.map((row) => (
        <div key={row.id} className="timeline-row">
          <span className="timeline-label">{row.label}</span>
          <div className="timeline-track" onClick={seekAt} data-testid={`track-${row.id}`}>
            {row.spans
              .filter((s) => s.end > start && s.start < end)
              .map((span) => (
                <div key={span.start} className="timeline-span" style={{ left: percent(span.start), width: width(span.start, span.end) }} />
              ))}
            {position !== null && position >= start && position <= end && <div className="timeline-playhead" style={{ left: percent(position) }} />}
          </div>
        </div>
      ))}
      <div className="timeline-ticks" aria-hidden="true">
        {ticks.map((t) => (
          <span key={t} style={{ left: percent(t) }}>
            {formatClock(t, duration < 10 * 60_000)}
          </span>
        ))}
      </div>
    </div>
  );
});
