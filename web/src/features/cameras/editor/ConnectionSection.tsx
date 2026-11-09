import type { Site } from '../../../api/types';
import { CheckboxField, RadioGroup, Section, SelectField, TextField } from '../../../shared/form/fields';
import { ErrorMessage } from '../../../shared/ui/ErrorMessage';
import { useCameraTest } from '../../../data/cameras';
import { toCamera, type SectionProps } from './cameraForm';
import { sourceOptions } from './options';
import { ProbeReport } from './ProbeReport';

export function ConnectionSection({ form, set, cameraId, sites }: SectionProps & { cameraId?: string; sites: readonly Site[] }) {
  const probe = useCameraTest();
  const url = form.source === 'onvif' ? form.onvifUrl : form.rtspUrl;

  return (
    <Section title="Connection">
      <div className="row">
        <TextField label="Name" required maxLength={100} value={form.name} onChange={(v) => set('name', v)} placeholder="Front door" />
        <SelectField label="Site" value={form.siteId} options={sites.map((s) => ({ value: s.id, label: s.name }))} onChange={(v) => set('siteId', v)} />
        <CheckboxField label="Record this camera" checked={form.enabled} onChange={(v) => set('enabled', v)} />
      </div>
      <RadioGroup legend="Connect with" value={form.source} options={sourceOptions} onChange={(v) => set('source', v)} />
      {form.source === 'onvif' ? (
        <TextField
          label="Device service URL"
          type="url"
          required
          value={form.onvifUrl}
          onChange={(v) => set('onvifUrl', v)}
          placeholder="http://192.168.1.20/onvif/device_service"
        />
      ) : (
        <TextField label="RTSP URL" required value={form.rtspUrl} onChange={(v) => set('rtspUrl', v)} placeholder="rtsp://192.168.1.20:554/stream1" />
      )}
      <div className="row">
        <TextField label="User name" autoComplete="off" value={form.username} onChange={(v) => set('username', v)} />
        <TextField
          label="Password"
          type="password"
          autoComplete="new-password"
          value={form.password}
          disabled={form.removePassword}
          placeholder={form.hasPassword ? 'Unchanged' : ''}
          onChange={(v) => set('password', v)}
        />
        {form.hasPassword && <CheckboxField label="Remove the stored password" checked={form.removePassword} onChange={(v) => set('removePassword', v)} />}
      </div>
      <div className="actions start">
        <button type="button" onClick={() => probe.mutate({ ...toCamera(form), id: cameraId })} disabled={probe.isPending || !url}>
          {probe.isPending ? 'Testing…' : 'Test connection'}
        </button>
      </div>
      <ErrorMessage error={probe.error} />
      {probe.data && <ProbeReport result={probe.data} profileToken={form.profileToken} onSelectProfile={(token) => set('profileToken', token)} />}
    </Section>
  );
}
