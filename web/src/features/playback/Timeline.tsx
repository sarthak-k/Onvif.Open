import { memo, useMemo, type KeyboardEvent, type MouseEvent } from 'react';
import type { TimelineEvent } from '../../data/recordings';
import { describeEvent, eventSeverity } from './events';
import type { Span } from './spans';
import { formatClock } from './time';

interface Props {
  start: number;
  end: number;
  spans: readonly Span[];
  events: readonly TimelineEvent[];
  position: number | null;
  clip: { from: number | null; to: number | null };
  onSeek(time: number): void;
}

/** Tick spacing for the window length: about 6 to 12 labels. */
function tickStep(duration: number): number {
  const minute = 60_000;
  const steps = [minute, 5 * minute, 10 * minute, 15 * minute, 30 * minute, 60 * minute, 2 * 60 * minute, 3 * 60 * minute];
  return steps.find((step) => duration / step <= 12) ?? steps[steps.length - 1];
}

/**
 * The window of the day as a bar: recorded spans, events, the clip being cut and the playhead. Click (or the arrow
 * keys: 10 s, with Shift 1 min) to move.
 */
export const Timeline = memo(function Timeline({ start, end, spans, events, position, clip, onSeek }: Props) {
  const duration = end - start;
  const percent = (time: number) => `${(Math.min(Math.max(time, start), end) - start) / duration * 100}%`;
  const width = (from: number, to: number) => `${(Math.min(to, end) - Math.max(from, start)) / duration * 100}%`;

  const ticks = useMemo(() => {
    const step = tickStep(duration);
    const first = Math.ceil(start / step) * step;
    const result: number[] = [];
    for (let t = first; t <= end; t += step) result.push(t);
    return result;
  }, [start, end, duration]);

  const visible = spans.filter((s) => s.end > start && s.start < end);
  const onClick = (event: MouseEvent<HTMLDivElement>) => {
    const box = event.currentTarget.getBoundingClientRect();
    onSeek(start + ((event.clientX - box.left) / box.width) * duration);
  };
  const onKey = (event: KeyboardEvent) => {
    const step = event.shiftKey ? 60_000 : 10_000;
    const now = position ?? start;
    const moves: Record<string, number> = { ArrowLeft: now - step, ArrowRight: now + step, Home: start, End: end - 1000 };
    if (event.key in moves) {
      event.preventDefault();
      onSeek(moves[event.key]);
    }
  };

  return (
    <div className="timeline">
      <div
        className="timeline-track"
        role="slider"
        tabIndex={0}
        aria-label="Time"
        aria-valuemin={start}
        aria-valuemax={end}
        aria-valuenow={position ?? start}
        aria-valuetext={position ? formatClock(position) : 'Not playing'}
        onClick={onClick}
        onKeyDown={onKey}
      >
        {visible.map((span) => (
          <div key={span.start} className="timeline-span" style={{ left: percent(span.start), width: width(span.start, span.end) }} />
        ))}
        {clip.from !== null && clip.to !== null && clip.to > clip.from && (
          <div className="timeline-clip" style={{ left: percent(clip.from), width: width(clip.from, clip.to) }} />
        )}
        {events
          .filter((e) => e.time.getTime() >= start && e.time.getTime() < end)
          .map((e, i) => (
            <div key={i} className={`timeline-event ${eventSeverity(e)}`} style={{ left: percent(e.time.getTime()) }} title={`${formatClock(e.time.getTime())} ${describeEvent(e)}`} />
          ))}
        {position !== null && position >= start && position <= end && <div className="timeline-playhead" style={{ left: percent(position) }} />}
      </div>
      <div className="timeline-ticks" aria-hidden="true">
        {ticks.map((t) => (
          <span key={t} style={{ left: percent(t) }}>
            {formatClock(t, false)}
          </span>
        ))}
      </div>
      {visible.length === 0 && <p className="muted small">Nothing recorded in this time.</p>}
    </div>
  );
});
