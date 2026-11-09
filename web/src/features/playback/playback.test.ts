import type { RecordingSegment, StoredEvent } from '../../api/types';
import { dateAt, timeAt } from './engine/timeMap';
import { windowOf } from './events';
import { nextRecorded, recordedSpans } from './spans';

const t0 = Date.UTC(2026, 10, 9, 12, 0, 0);
const segment = (startS: number, endS: number): RecordingSegment => ({
  cameraId: 'cam',
  fileName: `${startS}.ts`,
  startTime: new Date(t0 + startS * 1000).toISOString(),
  lastWriteTime: new Date(t0 + endS * 1000).toISOString(),
  size: 1,
  isActive: false,
  isLocal: true,
  isArchived: false,
});

describe('playback building blocks', () => {
  it('maps video time to the wall clock and back, across gaps', () => {
    const fragments = [
      { start: 0, duration: 10, programDateTime: t0 },
      { start: 10, duration: 10, programDateTime: t0 + 60_000 },
    ];
    expect(dateAt(fragments, 5)).toBe(t0 + 5000);
    expect(dateAt(fragments, 12)).toBe(t0 + 62_000);
    expect(timeAt(fragments, t0 + 3000)).toBe(3);
    // In the gap: the next recorded moment.
    expect(timeAt(fragments, t0 + 30_000)).toBe(10);
    expect(timeAt(fragments, t0 - 1000)).toBeNull();
    expect(timeAt(fragments, t0 + 71_000)).toBeNull();
  });

  it('merges files into recorded spans and finds the next recorded moment', () => {
    const spans = recordedSpans([segment(60, 120), segment(0, 59), segment(300, 360)]);
    expect(spans).toEqual([
      { start: t0, end: t0 + 120_000 },
      { start: t0 + 300_000, end: t0 + 360_000 },
    ]);
    expect(nextRecorded(spans, t0 + 10_000)).toBe(t0 + 10_000);
    expect(nextRecorded(spans, t0 + 200_000)).toBe(t0 + 300_000);
    expect(nextRecorded(spans, t0 + 400_000)).toBeNull();
  });

  it('plays from before an event to a minute after it', () => {
    const event = { startTime: new Date(t0).toISOString(), endTime: new Date(t0 + 10_000).toISOString() } as StoredEvent;
    expect(windowOf(event, t0 + 3_600_000)).toEqual({ from: t0 - 30_000, to: t0 + 90_000 });
    // Still going on: up to now.
    expect(windowOf({ ...event, endTime: null }, t0 + 20_000)).toEqual({ from: t0 - 30_000, to: t0 + 30_000 });
  });
});
