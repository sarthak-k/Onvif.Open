import { useState, type FormEvent } from 'react';
import type { AlertKind, AlertRule, EventCategory, Severity } from '../../api/types';
import { useAlertRules, useDeleteAlertRule, useSaveAlertRule, useTestAlertRule } from '../../data/alerts';
import { useCameraStatus } from '../../data/cameras';
import { useSites } from '../../data/tenancy';
import { CheckboxField, NumberField, RadioGroup, SelectField, TextAreaField, TextField } from '../../shared/form/fields';
import { useForm } from '../../shared/form/useForm';
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { categoryLabels } from '../playback/events';
import { describeDelivery, describeRule, formOf, formProblem, requestOf } from './alertRuleForm';
import { ScheduleEditor } from './ScheduleEditor';
import { scheduleProblem } from './schedule';

const kindOptions = [
  { value: 'Event' as AlertKind, label: 'When an event happens' },
  { value: 'Offline' as AlertKind, label: 'When a camera is offline for a while' },
];

const severityOptions = [
  { value: '' as const, label: 'Any severity' },
  { value: 'Warning' as const, label: 'Warning or worse' },
  { value: 'Critical' as const, label: 'Critical only' },
];

const categories = (Object.keys(categoryLabels) as EventCategory[]).filter((c) => c !== 'other');

const toggle = <T,>(list: T[], value: T) => (list.includes(value) ? list.filter((v) => v !== value) : [...list, value]);

/** When to raise alerts and whom to tell; alerts land in the inbox and go out by email and webhook. */
export default function AlertRulesPage() {
  const rules = useAlertRules();
  const remove = useDeleteAlertRule();
  const test = useTestAlertRule();
  const sites = useSites();
  const [editing, setEditing] = useState<AlertRule | 'new' | null>(null);
  const [deleting, setDeleting] = useState<AlertRule | null>(null);
  const [tested, setTested] = useState<{ rule: string; result: string } | null>(null);
  const siteNames = (ids: string[]) => (ids.length === 0 ? 'All sites' : ids.map((id) => sites.data?.find((s) => s.id === id)?.name ?? id).join(', '));

  function runTest(rule: AlertRule) {
    setTested(null);
    test.mutate(rule.id, {
      onSuccess: ({ delivery }) => {
        const results = Object.entries(delivery).map(([name, r]) => `${name}: ${r.ok ? 'sent' : `failed (${r.error ?? 'unknown error'})`}`);
        setTested({ rule: rule.name, result: results.length === 0 ? 'No email or webhook to send to.' : results.join('; ') });
      },
    });
  }

  return (
    <section className="stack">
      <div className="heading">
        <h1>Alert rules</h1>
        <button type="button" className="primary" onClick={() => setEditing('new')}>
          Add rule
        </button>
      </div>
      <p className="muted">Each rule raises alerts in the Alerts inbox, and can email people or post to a webhook. Hours are in each site's time zone.</p>
      <ErrorMessage error={rules.error ?? remove.error ?? test.error} />
      {tested && (
        <p className="card" role="status">
          Test of {tested.rule}: {tested.result}
        </p>
      )}

      {rules.isSuccess && rules.data.length === 0 ? (
        <p className="muted">No rules yet. Add one to be told when a person is seen at night, or when a camera goes offline.</p>
      ) : (
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Rule</th>
                <th>When</th>
                <th>Where</th>
                <th>Tells</th>
                <th>
                  <span className="sr-only">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {rules.data?.map((rule) => (
                <tr key={rule.id}>
                  <td>
                    {rule.name}
                    {!rule.enabled && <span className="badge">off</span>}
                  </td>
                  <td>{describeRule(rule)}</td>
                  <td>{rule.cameraIds.length > 0 ? `${rule.cameraIds.length} camera${rule.cameraIds.length === 1 ? '' : 's'}` : siteNames(rule.siteIds)}</td>
                  <td>{describeDelivery(rule)}</td>
                  <td className="row-actions">
                    <button type="button" onClick={() => setEditing(rule)}>
                      Edit
                    </button>
                    <button type="button" disabled={test.isPending} onClick={() => runTest(rule)}>
                      Test
                    </button>
                    <button type="button" className="danger" onClick={() => setDeleting(rule)}>
                      Delete
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {editing && <RuleDialog rule={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />}
      {deleting && (
        <ConfirmDialog
          title={`Delete rule ${deleting.name}?`}
          confirmLabel="Delete"
          danger
          onCancel={() => setDeleting(null)}
          onConfirm={() => {
            remove.mutate(deleting.id);
            setDeleting(null);
          }}
        >
          Alerts it raised stay in the inbox.
        </ConfirmDialog>
      )}
    </section>
  );
}

function RuleDialog({ rule, onClose }: { rule?: AlertRule; onClose(): void }) {
  const save = useSaveAlertRule();
  const sites = useSites();
  const cameras = useCameraStatus();
  const { values, set } = useForm(formOf(rule));
  const problem = formProblem(values) ?? (values.limitHours ? scheduleProblem(values.hours) : null);
  const shownCameras = (cameras.data ?? []).filter((c) => values.siteIds.length === 0 || (c.siteId != null && values.siteIds.includes(c.siteId)));

  function submit(event?: FormEvent) {
    event?.preventDefault();
    if (problem) return;
    save.mutate({ id: rule?.id ?? null, rule: requestOf(values) }, { onSuccess: onClose });
  }

  return (
    <ConfirmDialog title={rule ? `Edit ${rule.name}` : 'Add alert rule'} confirmLabel="Save" busy={save.isPending || problem !== null} onCancel={onClose} onConfirm={() => submit()}>
      <form className="stack" onSubmit={submit} aria-label="Alert rule">
        <TextField label="Name" required maxLength={100} placeholder="Person at night" value={values.name} onChange={(v) => set('name', v)} />
        <CheckboxField label="On" checked={values.enabled} onChange={(v) => set('enabled', v)} />
        <RadioGroup legend="Alert" value={values.kind} options={kindOptions} onChange={(v) => set('kind', v)} />

        {values.kind === 'Event' ? (
          <>
            <fieldset className="stack" aria-label="Events">
              <legend>Events</legend>
              <div className="day-toggles">
                {categories.map((c) => (
                  <CheckboxField key={c} label={categoryLabels[c]} checked={values.categories.includes(c)} onChange={() => set('categories', toggle(values.categories, c))} />
                ))}
              </div>
            </fieldset>
            <SelectField<Severity | ''> label="Severity" value={values.minSeverity} options={severityOptions} onChange={(v) => set('minSeverity', v)} />
          </>
        ) : (
          <NumberField label="Minutes offline before it alerts" step="1" min={1} value={values.offlineMinutes} onChange={(v) => set('offlineMinutes', v)} />
        )}

        <fieldset className="stack" aria-label="Sites">
          <legend>Sites (none: all)</legend>
          <div className="day-toggles">
            {(sites.data ?? []).map((s) => (
              <CheckboxField key={s.id} label={s.name} checked={values.siteIds.includes(s.id)} onChange={() => set('siteIds', toggle(values.siteIds, s.id))} />
            ))}
          </div>
        </fieldset>
        {shownCameras.length > 0 && (
          <fieldset className="stack" aria-label="Cameras">
            <legend>Cameras (none: all of the sites)</legend>
            <div className="day-toggles">
              {shownCameras.map((c) => (
                <CheckboxField key={c.cameraId} label={c.name} checked={values.cameraIds.includes(c.cameraId)} onChange={() => set('cameraIds', toggle(values.cameraIds, c.cameraId))} />
              ))}
            </div>
          </fieldset>
        )}

        <CheckboxField label="Only during set hours" checked={values.limitHours} onChange={(v) => set('limitHours', v)} />
        {values.limitHours && (
          <ScheduleEditor schedule={values.hours} onChange={(v) => set('hours', v)} hint="An end before the start runs past midnight. Outside these hours the rule does not alert." />
        )}
        <NumberField label="Quiet time after an alert, per camera (minutes)" step="1" min={0} value={values.cooldownMinutes} onChange={(v) => set('cooldownMinutes', v)} />

        <TextAreaField label="Email to (comma-separated)" rows={2} placeholder="guard@example.com" value={values.emails} onChange={(v) => set('emails', v)} />
        <TextField label="Webhook URL" type="url" placeholder="https://example.com/hooks/cameras" value={values.webhookUrl} onChange={(v) => set('webhookUrl', v)} />
        {values.webhookUrl.trim() && (
          <>
            <TextField
              label="Webhook secret (signs each post)"
              type="password"
              autoComplete="new-password"
              placeholder={rule?.hasWebhookSecret ? 'Unchanged' : 'Optional'}
              disabled={values.removeSecret}
              value={values.webhookSecret}
              onChange={(v) => set('webhookSecret', v)}
            />
            {rule?.hasWebhookSecret && <CheckboxField label="Remove the secret" checked={values.removeSecret} onChange={(v) => set('removeSecret', v)} />}
            <p className="muted small">Posts are JSON; with a secret, the header X-OnvifOpen-Signature carries sha256= and the HMAC-SHA256 of the body.</p>
          </>
        )}
        {problem && <p className="muted small">{problem}</p>}
        <ErrorMessage error={save.error} />
      </form>
    </ConfirmDialog>
  );
}
