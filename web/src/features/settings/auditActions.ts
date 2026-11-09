import type { AuditEntry } from '../../api/types';

/** What each recorded action means, in words. Unknown actions (from newer recorders) show as recorded. */
const actionLabels: Record<string, string> = {
  'organization.setup': 'Set up the organization',
  'organization.rename': 'Renamed the organization',
  'auth.signin': 'Signed in',
  'auth.signin_failed': 'Failed to sign in',
  'auth.password_change': 'Changed their password',
  'user.create': 'Added a user',
  'user.update': 'Changed a user',
  'user.delete': 'Removed a user',
  'site.create': 'Added a site',
  'site.update': 'Changed a site',
  'site.delete': 'Removed a site',
  'policy.create': 'Added a recording policy',
  'policy.update': 'Changed a recording policy',
  'policy.delete': 'Removed a recording policy',
  'camera.create': 'Added a camera',
  'camera.update': 'Changed a camera',
  'camera.delete': 'Removed a camera',
  'gateway.update': 'Changed a gateway',
  'plugin.update': 'Configured a plugin',
  'plugin.delete': 'Removed a plugin',
  'layout.create': 'Saved a layout',
  'layout.update': 'Changed a layout',
  'layout.delete': 'Removed a layout',
  'video.live': 'Watched live video',
  'video.playback': 'Watched recordings',
  'video.export': 'Exported video',
  'recording.delete': 'Deleted a recording',
  'recording.start': 'Started recording',
  'recording.stop': 'Stopped recording',
  'recording.trigger': 'Triggered recording',
  'io.relay': 'Switched a relay output',
};

/** The areas the log can be narrowed to (the part of the action before the dot). */
export const auditAreas = [
  { value: '', label: 'Everything' },
  { value: 'auth', label: 'Sign-ins' },
  { value: 'video', label: 'Video viewed or exported' },
  { value: 'camera', label: 'Cameras' },
  { value: 'recording', label: 'Recording' },
  { value: 'user', label: 'Users' },
  { value: 'site', label: 'Sites' },
  { value: 'policy', label: 'Recording policies' },
  { value: 'layout', label: 'Layouts' },
  { value: 'gateway', label: 'Gateways' },
  { value: 'plugin', label: 'Plugins' },
  { value: 'io', label: 'Device outputs' },
  { value: 'organization', label: 'Organization' },
] as const;

export function describeAction(action: string): string {
  return actionLabels[action] ?? action;
}

/** Details as "name: value" pairs when they are an object (also as JSON text), else as text. */
export function describeDetails(details: AuditEntry['details']): string {
  if (details === null || details === undefined || details === '') {
    return '';
  }

  let value = details;
  if (typeof value === 'string') {
    try {
      value = JSON.parse(value);
    } catch {
      return details as string;
    }
  }

  if (value && typeof value === 'object' && !Array.isArray(value)) {
    return Object.entries(value)
      .filter(([, item]) => item !== null && item !== undefined)
      .map(([name, item]) => `${name}: ${Array.isArray(item) ? item.join(', ') || 'none' : typeof item === 'object' ? JSON.stringify(item) : String(item)}`)
      .join('; ');
  }
  return typeof details === 'string' ? details : JSON.stringify(details);
}
