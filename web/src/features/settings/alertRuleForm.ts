import type { AlertKind, AlertRule, AlertRuleRequest, EventCategory, ScheduleWindow, Severity } from '../../api/types';
import { categoryLabels } from '../playback/events';
import { describeSchedule } from './schedule';

/** The alert rule editor's values (text fields as typed). */
export interface AlertRuleForm {
  name: string;
  enabled: boolean;
  kind: AlertKind;
  categories: EventCategory[];
  minSeverity: Severity | '';
  offlineMinutes: string;
  siteIds: string[];
  cameraIds: string[];
  limitHours: boolean;
  hours: ScheduleWindow[];
  cooldownMinutes: string;
  emails: string;
  webhookUrl: string;
  /** Empty keeps the stored one. */
  webhookSecret: string;
  removeSecret: boolean;
}

const nightHours: ScheduleWindow[] = [{ days: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'], start: '22:00', end: '06:00' }];

export function formOf(rule?: AlertRule): AlertRuleForm {
  return {
    name: rule?.name ?? '',
    enabled: rule?.enabled ?? true,
    kind: rule?.kind ?? 'Event',
    categories: rule?.categories ?? ['person'],
    minSeverity: rule?.minSeverity ?? '',
    offlineMinutes: String(rule?.offlineMinutes ?? 5),
    siteIds: rule?.siteIds ?? [],
    cameraIds: rule?.cameraIds ?? [],
    limitHours: (rule?.hours.length ?? 0) > 0,
    hours: rule?.hours.length ? rule.hours : nightHours,
    cooldownMinutes: String(rule?.cooldownMinutes ?? 5),
    emails: (rule?.emails ?? []).join(', '),
    webhookUrl: rule?.webhookUrl ?? '',
    webhookSecret: '',
    removeSecret: false,
  };
}

/** "a@x.com, b@y.com\nc@z.com" → ["a@x.com", "b@y.com", "c@z.com"]. */
export function emailsOf(text: string): string[] {
  return text
    .split(/[\s,;]+/)
    .map((e) => e.trim())
    .filter(Boolean);
}

export function requestOf(form: AlertRuleForm): AlertRuleRequest {
  return {
    name: form.name.trim(),
    enabled: form.enabled,
    kind: form.kind,
    categories: form.kind === 'Event' ? form.categories : [],
    minSeverity: form.kind === 'Event' && form.minSeverity ? form.minSeverity : null,
    offlineMinutes: Number(form.offlineMinutes),
    siteIds: form.siteIds,
    cameraIds: form.cameraIds,
    hours: form.limitHours ? form.hours : [],
    cooldownMinutes: Number(form.cooldownMinutes),
    emails: emailsOf(form.emails),
    webhookUrl: form.webhookUrl.trim() || null,
    webhookSecret: form.removeSecret ? '' : form.webhookSecret || null,
  };
}

/** What is wrong with the form in words, or null when it can be saved (the recorder checks the rest). */
export function formProblem(form: AlertRuleForm): string | null {
  if (!form.name.trim()) return 'Give the rule a name.';
  if (form.kind === 'Event' && form.categories.length === 0) return 'Choose at least one kind of event.';
  const minutes = Number(form.offlineMinutes);
  if (form.kind === 'Offline' && (!Number.isInteger(minutes) || minutes < 1)) return 'A camera is offline for at least a minute before it alerts.';
  const cooldown = Number(form.cooldownMinutes);
  if (!Number.isInteger(cooldown) || cooldown < 0) return 'The quiet time is a whole number of minutes.';
  return null;
}

/** "Person or Vehicle, Warning or worse, 22:00–06:00 every day" / "Camera offline for 5 min". */
export function describeRule(rule: AlertRule): string {
  const what =
    rule.kind === 'Offline'
      ? `Camera offline for ${rule.offlineMinutes} min`
      : rule.categories.length === 0
        ? 'Any event'
        : rule.categories.map((c) => categoryLabels[c] ?? c).join(' or ');
  const parts = [what];
  if (rule.kind === 'Event' && rule.minSeverity && rule.minSeverity !== 'Info') parts.push(`${rule.minSeverity} or worse`);
  if (rule.hours.length > 0) parts.push(describeSchedule(rule.hours));
  return parts.join(', ');
}

/** "Email (2), webhook" / "Inbox only". */
export function describeDelivery(rule: AlertRule): string {
  const ways = [rule.emails.length > 0 && `Email (${rule.emails.length})`, rule.webhookUrl && 'webhook'].filter(Boolean);
  return ways.length === 0 ? 'Inbox only' : ways.join(', ');
}
