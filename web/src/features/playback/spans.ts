import type { RecordingSegment } from '../../api/types';

/** A stretch of time with recordings. */
export interface Span {
  start: number;
  end: number;
}

/** Segments merged into continuous spans; gaps up to `toleranceMs` (segment rotation) are bridged. */
export function recordedSpans(segments: readonly RecordingSegment[], toleranceMs = 2000): Span[] {
  const sorted = segments
    .map((s) => ({ start: Date.parse(s.startTime), end: Date.parse(s.lastWriteTime) }))
    .filter((s) => s.end > s.start)
    .sort((a, b) => a.start - b.start);

  const spans: Span[] = [];
  for (const segment of sorted) {
    const last = spans[spans.length - 1];
    if (last && segment.start <= last.end + toleranceMs) last.end = Math.max(last.end, segment.end);
    else spans.push({ ...segment });
  }
  return spans;
}

/** The time itself when recorded, else the start of the next recording; null when nothing follows. */
export function nextRecorded(spans: readonly Span[], time: number): number | null {
  for (const span of spans) {
    if (time < span.end) return Math.max(time, span.start);
  }
  return null;
}

/** The start of the last recording that begins before `time`, or null. */
export function previousRecordingStart(spans: readonly Span[], time: number): number | null {
  let found: number | null = null;
  for (const span of spans) {
    if (span.start < time) found = span.start;
  }
  return found;
}
