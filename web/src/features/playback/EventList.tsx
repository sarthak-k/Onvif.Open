import { memo } from 'react';
import type { TimelineEvent } from '../../data/recordings';
import { describeEvent, eventSeverity } from './events';
import { formatClock } from './time';

/** The day's events; choosing one plays from a few seconds before it. */
export const EventList = memo(function EventList({ events, onSelect }: { events: readonly TimelineEvent[]; onSelect(event: TimelineEvent): void }) {
  if (events.length === 0) return <p className="empty">No events on this day.</p>;

  return (
    <ol className="event-list" aria-label="Events">
      {events.map((event, i) => (
        <li key={i}>
          <button type="button" className={`event ${eventSeverity(event)}`} onClick={() => onSelect(event)}>
            <time dateTime={event.time.toISOString()}>{formatClock(event.time.getTime())}</time> {describeEvent(event)}
          </button>
        </li>
      ))}
    </ol>
  );
});
