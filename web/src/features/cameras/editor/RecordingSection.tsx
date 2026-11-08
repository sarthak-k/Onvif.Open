import { NumberField, RadioGroup, Section } from '../../../shared/form/fields';
import type { SectionProps } from './cameraForm';
import { modeOptions } from './options';

export function RecordingSection({ form, set }: SectionProps) {
  return (
    <Section title="Recording">
      <RadioGroup legend="Mode" value={form.mode} options={modeOptions} onChange={(v) => set('mode', v)} />
      {form.mode === 'Triggered' && (
        <div className="row">
          <NumberField label="Pre-roll (seconds)" value={form.preRollSeconds} onChange={(v) => set('preRollSeconds', v)} />
          <NumberField label="Post-roll (seconds)" value={form.postRollSeconds} onChange={(v) => set('postRollSeconds', v)} />
        </div>
      )}
      <div className="row">
        <NumberField label="Segment length (minutes)" value={form.segmentMinutes} onChange={(v) => set('segmentMinutes', v)} placeholder="Recorder default" />
        <NumberField label="Max segment size (MB)" value={form.maxSegmentSizeMB} onChange={(v) => set('maxSegmentSizeMB', v)} placeholder="Recorder default" />
        <NumberField label="Align segments to (minutes)" value={form.alignToClockMinutes} onChange={(v) => set('alignToClockMinutes', v)} placeholder="Off" />
      </div>
    </Section>
  );
}
