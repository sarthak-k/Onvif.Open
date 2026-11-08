import { CheckboxField, Section, SelectField, TextField } from '../../../shared/form/fields';
import type { SectionProps } from './cameraForm';
import { mediaVersionOptions, transportOptions } from './options';

export function StreamSection({ form, set }: SectionProps) {
  const onvif = form.source === 'onvif';
  return (
    <Section title="Stream">
      <div className="row">
        {onvif && <TextField label="Media profile token" value={form.profileToken} onChange={(v) => set('profileToken', v)} placeholder="First profile" />}
        <SelectField label="Transport" value={form.transport} options={transportOptions} onChange={(v) => set('transport', v)} />
        {onvif && <SelectField label="Media service" value={form.mediaVersion} options={mediaVersionOptions} onChange={(v) => set('mediaVersion', v)} />}
      </div>
      <CheckboxField label="Record audio (AAC)" checked={form.audioEnabled} onChange={(v) => set('audioEnabled', v)} />
    </Section>
  );
}
