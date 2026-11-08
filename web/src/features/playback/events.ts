import type { TimelineEvent } from '../../data/recordings';

/** A short, readable name for an event: "Motion started", "Person detected: two faces". */
export function describeEvent(item: TimelineEvent): string {
  if (item.kind === 'camera') {
    const name = item.event.topicPath.split('/').filter(Boolean).pop() ?? item.event.topic;
    const state = item.event.state === true ? ' started' : item.event.state === false ? ' ended' : '';
    return `${name}${state}`;
  }
  return item.event.message ? `${item.event.type}: ${item.event.message}` : item.event.type;
}

export function eventSeverity(item: TimelineEvent): 'info' | 'warning' | 'critical' {
  return item.kind === 'plugin' ? (item.event.severity.toLowerCase() as 'info' | 'warning' | 'critical') : 'info';
}

/** Seconds of video shown before an event when jumping to it. */
export const eventLeadMs = 5000;
