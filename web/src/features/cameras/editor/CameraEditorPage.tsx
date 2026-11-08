import { useCallback, useEffect, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import type { DiscoveredDevice } from '../../../api/types';
import { useCameraConfig, useSaveCamera } from '../../../data/cameras';
import { useForm } from '../../../shared/form/useForm';
import { ErrorMessage } from '../../../shared/ui/ErrorMessage';
import { emptyCameraForm, suggestId, toCamera, toForm } from './cameraForm';
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
  const { values: form, set, setValues } = useForm(emptyCameraForm);

  useEffect(() => {
    if (existing.data) setValues(toForm(existing.data));
  }, [existing.data, setValues]);

  const useDevice = useCallback(
    (device: DiscoveredDevice, taken: string[]) =>
      setValues((f) => ({
        ...f,
        source: 'onvif',
        onvifUrl: device.deviceServiceUri ?? f.onvifUrl,
        id: f.id || suggestId(device.name, device.deviceServiceUri, taken),
      })),
    [setValues],
  );

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate(toCamera(form), { onSuccess: () => navigate('/cameras') });
  }

  if (existing.isLoading) return <p className="loading">Loading…</p>;
  if (existing.error) return <ErrorMessage error={existing.error} />;

  return (
    <section className="stack">
      <div className="heading">
        <h1>{isNew ? 'Add camera' : `Camera ${id}`}</h1>
        <Link to="/cameras">Back to cameras</Link>
      </div>

      {isNew && <DiscoveryPanel onUse={useDevice} />}

      <form onSubmit={submit} className="stack" aria-label="Camera">
        <ConnectionSection form={form} set={set} isNew={isNew} />
        <StreamSection form={form} set={set} />
        <RecordingSection form={form} set={set} />
        {form.source === 'onvif' && <EventsSection form={form} set={set} />}

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
