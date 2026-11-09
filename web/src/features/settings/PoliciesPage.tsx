import { useState, type FormEvent } from 'react';
import type { PolicyMode, RecordingPolicy } from '../../api/types';
import { useDeletePolicy, usePolicies, useSavePolicy } from '../../data/tenancy';
import { CheckboxField, NumberField, RadioGroup, TextField } from '../../shared/form/fields';
import { useForm } from '../../shared/form/useForm';
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { describePolicy } from '../cameras/editor/RecordingSection';
import { ScheduleEditor } from './ScheduleEditor';
import { businessHours, scheduleProblem } from './schedule';

const modeOptions = [
  { value: 'Always' as PolicyMode, label: 'Record always' },
  { value: 'EventsOnly' as PolicyMode, label: 'Record around events only' },
  { value: 'Schedule' as PolicyMode, label: 'Record always during set hours, around events otherwise' },
];

/** How cameras record and how long video is kept; a change applies to its cameras at once. */
export default function PoliciesPage() {
  const policies = usePolicies();
  const remove = useDeletePolicy();
  const [editing, setEditing] = useState<RecordingPolicy | 'new' | null>(null);
  const [deleting, setDeleting] = useState<RecordingPolicy | null>(null);

  return (
    <section className="stack">
      <div className="heading">
        <h1>Recording policies</h1>
        <button type="button" className="primary" onClick={() => setEditing('new')}>
          Add policy
        </button>
      </div>
      <p className="muted">Every camera follows one policy. New cameras get the default one.</p>
      <ErrorMessage error={policies.error ?? remove.error} />

      <div className="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Policy</th>
              <th>What it does</th>
              <th>
                <span className="sr-only">Actions</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {policies.data?.map((policy) => (
              <tr key={policy.id}>
                <td>
                  {policy.name}
                  {policy.isDefault && <span className="badge">default</span>}
                </td>
                <td>{describePolicy(policy)}</td>
                <td className="row-actions">
                  <button type="button" onClick={() => setEditing(policy)}>
                    Edit
                  </button>
                  <button type="button" className="danger" disabled={policy.isDefault} onClick={() => setDeleting(policy)}>
                    Delete
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {editing && <PolicyDialog policy={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />}
      {deleting && (
        <ConfirmDialog
          title={`Delete policy ${deleting.name}?`}
          confirmLabel="Delete"
          danger
          onCancel={() => setDeleting(null)}
          onConfirm={() => {
            remove.mutate(deleting.id);
            setDeleting(null);
          }}
        >
          Only a policy no camera uses can be deleted.
        </ConfirmDialog>
      )}
    </section>
  );
}

const number = (value: string) => Number(value);

function PolicyDialog({ policy, onClose }: { policy?: RecordingPolicy; onClose(): void }) {
  const save = useSavePolicy();
  const { values, set } = useForm({
    name: policy?.name ?? '',
    mode: policy?.mode ?? ('Always' as PolicyMode),
    preRollSeconds: String(policy?.preRollSeconds ?? 5),
    postRollSeconds: String(policy?.postRollSeconds ?? 10),
    continuousRetentionDays: String(policy?.continuousRetentionDays ?? 7),
    eventRetentionDays: String(policy?.eventRetentionDays ?? 30),
    isDefault: policy?.isDefault ?? false,
    schedule: policy?.schedule?.length ? policy.schedule : businessHours,
  });

  function submit(event?: FormEvent) {
    event?.preventDefault();
    save.mutate(
      {
        id: policy?.id,
        policy: {
          name: values.name,
          mode: values.mode,
          preRollSeconds: number(values.preRollSeconds),
          postRollSeconds: number(values.postRollSeconds),
          continuousRetentionDays: number(values.continuousRetentionDays),
          eventRetentionDays: number(values.eventRetentionDays),
          isDefault: values.isDefault,
          schedule: values.mode === 'Schedule' ? values.schedule : (policy?.schedule ?? []),
        },
      },
      { onSuccess: onClose },
    );
  }

  const events = values.mode !== 'Always';
  const scheduled = values.mode === 'Schedule';
  const problem = scheduled ? scheduleProblem(values.schedule) : null;

  return (
    <ConfirmDialog title={policy ? `Edit ${policy.name}` : 'Add policy'} confirmLabel="Save" busy={save.isPending || !values.name.trim() || problem !== null} onCancel={onClose} onConfirm={() => submit()}>
      <form className="stack" onSubmit={submit} aria-label="Recording policy">
        <TextField label="Name" required maxLength={64} value={values.name} onChange={(v) => set('name', v)} />
        <RadioGroup legend="Recording" value={values.mode} options={modeOptions} onChange={(v) => set('mode', v)} />
        {scheduled && <ScheduleEditor schedule={values.schedule} onChange={(v) => set('schedule', v)} hint="An end before the start runs past midnight. Outside these hours the camera records around events." />}
        {problem && <p className="muted small">{problem}</p>}
        {events && (
          <div className="row">
            <NumberField label="Seconds before an event" value={values.preRollSeconds} onChange={(v) => set('preRollSeconds', v)} step="1" />
            <NumberField label="Seconds after an event" value={values.postRollSeconds} onChange={(v) => set('postRollSeconds', v)} step="1" />
          </div>
        )}
        <div className="row">
          {values.mode !== 'EventsOnly' && <NumberField label="Keep video (days)" value={values.continuousRetentionDays} onChange={(v) => set('continuousRetentionDays', v)} step="1" />}
          <NumberField label="Keep video around events (days)" value={values.eventRetentionDays} onChange={(v) => set('eventRetentionDays', v)} step="1" />
        </div>
        <CheckboxField label="Default for new cameras" checked={values.isDefault} disabled={policy?.isDefault} onChange={(v) => set('isDefault', v)} />
        {policy && <p className="muted small">Cameras using this policy reconnect with the change.</p>}
        <ErrorMessage error={save.error} />
      </form>
    </ConfirmDialog>
  );
}
