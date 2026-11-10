import { useCallback, useEffect, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import type { DiscoveredDevice } from '../../../api/types';
import { useCameraConfig, useSaveCamera } from '../../../data/cameras';
import { useGateways } from '../../../data/gateways';
import { usePolicies, useSites } from '../../../data/tenancy';
import { useForm } from '../../../shared/form/useForm';
import { ErrorMessage } from '../../../shared/ui/ErrorMessage';
import { emptyCameraForm, suggestName, toCamera, toForm } from './cameraForm';
import { ConnectionSection } from './ConnectionSection';
import { DiscoveryPanel } from './DiscoveryPanel';
import { EventsSection } from './EventsSection';
import { RecordingSection } from './RecordingSection';
import { StreamSection } from './StreamSection';

/** Adds a camera (/cameras/new) or edits one (/cameras/:id); each section owns its fields. */
export default function CameraEditorPage() {
  const { id } = useParams();
  const isNew = id === undefined;
  const navigate = useNavigate();
  const existing = useCameraConfig(id);
  const save = useSaveCamera(id);
  const sites = useSites();
  const gateways = useGateways();
  const policies = usePolicies();
  const { values: form, set, setValues } = useForm(emptyCameraForm);

  useEffect(() => {
    if (existing.data) setValues(toForm(existing.data));
  }, [existing.data, setValues]);

  // A new camera goes to the first site unless another is chosen.
  useEffect(() => {
    if (isNew && sites.data?.length) setValues((f) => (f.siteId ? f : { ...f, siteId: sites.data[0].id }));
  }, [isNew, sites.data, setValues]);

  const policy = policies.data?.find((p) => p.id === form.policyId) ?? policies.data?.find((p) => p.isDefault);

  const useDevice = useCallback(
    (device: DiscoveredDevice, taken: string[]) =>
      setValues((f) => ({
        ...f,
        source: 'onvif',
        onvifUrl: device.deviceServiceUri ?? f.onvifUrl,
        name: f.name || suggestName(device.name, device.deviceServiceUri, taken),
      })),
    [setValues],
  );

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate(toCamera(form), { onSuccess: () => navigate('/cameras') });
  }

  if (existing.isLoading || sites.isLoading || policies.isLoading) return <p className="loading">Loading…</p>;
  if (existing.error || sites.error || policies.error) return <ErrorMessage error={existing.error ?? sites.error ?? policies.error} />;

  return (
    <section className="stack">
      <div className="heading">
        <h1>{isNew ? 'Add camera' : (existing.data?.name ?? 'Camera')}</h1>
        <Link to="/cameras">Back to cameras</Link>
      </div>

      {isNew && <DiscoveryPanel onUse={useDevice} />}

      <form onSubmit={submit} className="stack" aria-label="Camera">
        <ConnectionSection form={form} set={set} cameraId={id} sites={sites.data ?? []} gateways={gateways.data ?? []} />
        <StreamSection form={form} set={set} />
        <RecordingSection form={form} set={set} policies={policies.data ?? []} />
        {form.source === 'onvif' && <EventsSection form={form} set={set} triggered={policy?.mode === 'EventsOnly'} />}

        <ErrorMessage error={save.error} />
        <div className="actions">
          <Link className="button" to="/cameras">
            Cancel
          </Link>
          <button type="submit" className="primary" disabled={save.isPending}>
            {isNew ? 'Add camera' : 'Save'}
          </button>
        </div>
      </form>
    </section>
  );
}
