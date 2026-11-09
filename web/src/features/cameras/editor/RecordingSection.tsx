import { Link } from 'react-router-dom';
import type { RecordingPolicy } from '../../../api/types';
import { NumberField, Section, SelectField } from '../../../shared/form/fields';
import type { SectionProps } from './cameraForm';

/** "Records always · keeps video 7 days, around events 30 days" */
export function describePolicy(policy: RecordingPolicy): string {
  const how = policy.mode === 'Always' ? 'Records always' : `Records around events (${policy.preRollSeconds} s before, ${policy.postRollSeconds} s after)`;
  const keep = policy.mode === 'Always' ? `keeps video ${policy.continuousRetentionDays} days, around events ${policy.eventRetentionDays} days` : `keeps video ${policy.eventRetentionDays} days`;
  return `${how} · ${keep}`;
}

/** How the camera records comes from its recording policy; only file splitting stays per camera. */
export function RecordingSection({ form, set, policies }: SectionProps & { policies: readonly RecordingPolicy[] }) {
  const options = policies.map((p) => ({ value: p.id, label: p.isDefault ? `${p.name} (default)` : p.name }));
  const chosen = policies.find((p) => p.id === form.policyId) ?? policies.find((p) => p.isDefault);

  return (
    <Section title="Recording">
      <SelectField label="Recording policy" value={chosen?.id ?? ''} options={options} onChange={(v) => set('policyId', v)} />
      {chosen && <p className="muted small">{describePolicy(chosen)}. Policies are managed in <Link to="/settings/policies">Settings</Link>.</p>}
      <details>
        <summary className="small">Advanced: recording files</summary>
        <div className="row">
          <NumberField label="Segment length (minutes)" value={form.segmentMinutes} onChange={(v) => set('segmentMinutes', v)} placeholder="Recorder default" />
          <NumberField label="Max segment size (MB)" value={form.maxSegmentSizeMB} onChange={(v) => set('maxSegmentSizeMB', v)} placeholder="Recorder default" />
          <NumberField label="Align segments to (minutes)" value={form.alignToClockMinutes} onChange={(v) => set('alignToClockMinutes', v)} placeholder="Off" />
        </div>
      </details>
    </Section>
  );
}
