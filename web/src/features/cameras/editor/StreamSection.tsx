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
      <div className="row">
        {onvif ? (
          <TextField label="Smaller stream for grids (profile token)" value={form.subProfileToken} onChange={(v) => set('subProfileToken', v)} placeholder="Same as recorded" />
        ) : (
          <TextField label="Smaller stream for grids" value={form.subRtspUrl} onChange={(v) => set('subRtspUrl', v)} placeholder="rtsp://…/sub (optional)" />
        )}
      </div>
      <p className="muted small">Grids of cameras play this smaller stream (opened only while someone watches); an enlarged camera plays the recorded one.</p>
      <CheckboxField label="Record audio (AAC)" checked={form.audioEnabled} onChange={(v) => set('audioEnabled', v)} />
    </Section>
  );
}
