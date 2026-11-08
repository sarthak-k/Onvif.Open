import { useEffect, useState, type FormEvent, type ReactNode } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { DiscoveredDevice, ProbeResult } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorMessage } from '../components/ErrorMessage';
import { emptyCameraForm, suggestId, toCamera, toForm, type CameraForm } from './cameraForm';

type Setter = <K extends keyof CameraForm>(key: K, value: CameraForm[K]) => void;

export function CameraEditorPage() {
  const { id } = useParams();
  const isNew = id === undefined;
  const { api } = useAuth();
  const navigate = useNavigate();
  const queries = useQueryClient();

  const existing = useQuery({ queryKey: ['camera-configs', id], queryFn: () => api.cameras.get(id!), enabled: !isNew });
  const [form, setForm] = useState<CameraForm>(emptyCameraForm);
  const set: Setter = (key, value) => setForm((f) => ({ ...f, [key]: value }));
  useEffect(() => {
    if (existing.data) setForm(toForm(existing.data));
  }, [existing.data]);

  const save = useMutation({
    mutationFn: () => (isNew ? api.cameras.create(toCamera(form)) : api.cameras.update(id!, toCamera(form))),
    onSuccess: () => {
      void queries.invalidateQueries({ queryKey: ['camera-configs'] });
      void queries.invalidateQueries({ queryKey: ['camera-status'] });
      navigate('/cameras');
    },
  });

  const probe = useMutation({ mutationFn: () => api.cameras.test(toCamera(form)) });

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate();
  }

  if (existing.isLoading) return <p className="loading">Loading…</p>;
  if (existing.error) return <ErrorMessage error={existing.error} />;

  return (
    <section className="stack">
      <div className="heading">
        <h1>{isNew ? 'Add camera' : `Camera ${id}`}</h1>
        <Link to="/cameras">Back to cameras</Link>
      </div>

      {isNew && (
        <Discovery
          onUse={(device, taken) =>
            setForm((f) => ({
              ...f,
              source: 'onvif',
              onvifUrl: device.deviceServiceUri ?? f.onvifUrl,
              id: f.id || suggestId(device.name, device.deviceServiceUri, taken),
            }))
          }
        />
      )}

      <form onSubmit={submit} className="stack" aria-label="Camera">
        <Section title="Connection">
          <div className="row">
            <label>
              Id
              <input required disabled={!isNew} value={form.id} onChange={(e) => set('id', e.target.value)} placeholder="front-door" />
            </label>
            <label className="check">
              <input type="checkbox" checked={form.enabled} onChange={(e) => set('enabled', e.target.checked)} />
              Record this camera
            </label>
          </div>
          <fieldset className="choice">
            <legend>Connect with</legend>
            <label className="check">
              <input type="radio" name="source" checked={form.source === 'onvif'} onChange={() => set('source', 'onvif')} />
              ONVIF device
            </label>
            <label className="check">
              <input type="radio" name="source" checked={form.source === 'rtsp'} onChange={() => set('source', 'rtsp')} />
              RTSP stream only
            </label>
          </fieldset>
          {form.source === 'onvif' ? (
            <label>
              Device service URL
              <input type="url" required value={form.onvifUrl} onChange={(e) => set('onvifUrl', e.target.value)} placeholder="http://192.168.1.20/onvif/device_service" />
            </label>
          ) : (
            <label>
              RTSP URL
              <input required value={form.rtspUrl} onChange={(e) => set('rtspUrl', e.target.value)} placeholder="rtsp://192.168.1.20:554/stream1" />
            </label>
          )}
          <div className="row">
            <label>
              User name
              <input autoComplete="off" value={form.username} onChange={(e) => set('username', e.target.value)} />
            </label>
            <label>
              Password
              <input
                type="password"
                autoComplete="new-password"
                value={form.password}
                disabled={form.removePassword}
                placeholder={form.hasPassword ? 'Unchanged' : ''}
                onChange={(e) => set('password', e.target.value)}
              />
            </label>
            {form.hasPassword && (
              <label className="check">
                <input type="checkbox" checked={form.removePassword} onChange={(e) => set('removePassword', e.target.checked)} />
                Remove the stored password
              </label>
            )}
          </div>
          <div className="actions start">
            <button type="button" onClick={() => probe.mutate()} disabled={probe.isPending || !(form.source === 'onvif' ? form.onvifUrl : form.rtspUrl)}>
              {probe.isPending ? 'Testing…' : 'Test connection'}
            </button>
          </div>
          <ErrorMessage error={probe.error} />
          {probe.data && <ProbeReport result={probe.data} form={form} set={set} />}
        </Section>

        <Section title="Stream">
          <div className="row">
            {form.source === 'onvif' && (
              <label>
                Media profile token
                <input value={form.profileToken} onChange={(e) => set('profileToken', e.target.value)} placeholder="First profile" />
              </label>
            )}
            <label>
              Transport
              <select value={form.transport} onChange={(e) => set('transport', e.target.value as CameraForm['transport'])}>
                <option value="Rtsp">RTP over RTSP (TCP)</option>
                <option value="Udp">RTP over UDP</option>
              </select>
            </label>
            {form.source === 'onvif' && (
              <label>
                Media service
                <select value={form.mediaVersion} onChange={(e) => set('mediaVersion', e.target.value as CameraForm['mediaVersion'])}>
                  <option value="Auto">Automatic</option>
                  <option value="Media1">Media (ver10)</option>
                  <option value="Media2">Media2 (ver20, H.265)</option>
                </select>
              </label>
            )}
          </div>
          <label className="check">
            <input type="checkbox" checked={form.audioEnabled} onChange={(e) => set('audioEnabled', e.target.checked)} />
            Record audio (AAC)
          </label>
        </Section>

        <Section title="Recording">
          <fieldset className="choice">
            <legend>Mode</legend>
            <label className="check">
              <input type="radio" name="mode" checked={form.mode === 'Continuous'} onChange={() => set('mode', 'Continuous')} />
              Continuous
            </label>
            <label className="check">
              <input type="radio" name="mode" checked={form.mode === 'Triggered'} onChange={() => set('mode', 'Triggered')} />
              On events and detections only
            </label>
          </fieldset>
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

        {form.source === 'onvif' && (
          <Section title="Events and analytics">
            <label className="check">
              <input type="checkbox" checked={form.eventsEnabled} onChange={(e) => set('eventsEnabled', e.target.checked)} />
              Receive camera events (motion, tampering, inputs)
            </label>
            {form.eventsEnabled && form.mode === 'Triggered' && (
              <label>
                Topics that trigger recording (one per line, empty: motion)
                <textarea rows={3} value={form.triggerTopics} onChange={(e) => set('triggerTopics', e.target.value)} placeholder="tns1:RuleEngine/CellMotionDetector/Motion" />
              </label>
            )}
            <label className="check">
              <input type="checkbox" checked={form.analyticsEnabled} onChange={(e) => set('analyticsEnabled', e.target.checked)} />
              Receive object detections (Profile M)
            </label>
            {form.analyticsEnabled && (
              <div className="row">
                {form.mode === 'Triggered' && (
                  <label>
                    Classes that trigger recording
                    <input value={form.triggerClasses} onChange={(e) => set('triggerClasses', e.target.value)} placeholder="Human, Vehicle" />
                  </label>
                )}
                <NumberField label="Minimum likelihood (0–1)" value={form.minLikelihood} onChange={(v) => set('minLikelihood', v)} step="0.05" />
                <label>
                  Analytics profile token
                  <input value={form.analyticsProfileToken} onChange={(e) => set('analyticsProfileToken', e.target.value)} placeholder="The recorded profile" />
                </label>
              </div>
            )}
          </Section>
        )}

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

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <fieldset className="card stack">
      <legend>{title}</legend>
      {children}
    </fieldset>
  );
}

function NumberField({ label, value, onChange, placeholder, step = 'any' }: { label: string; value: string; onChange(value: string): void; placeholder?: string; step?: string }) {
  return (
    <label>
      {label}
      <input type="number" min="0" step={step} value={value} placeholder={placeholder} onChange={(e) => onChange(e.target.value)} />
    </label>
  );
}

/** What the connection test found: device, capabilities, and the profiles to choose from. */
function ProbeReport({ result, form, set }: { result: ProbeResult; form: CameraForm; set: Setter }) {
  if (!result.ok) {
    return (
      <div role="alert" className="alert alert-error">
        {result.kind === 'unauthorized' ? 'The camera refused the user name or password. ' : 'The camera cannot be reached. '}
        {result.error}
      </div>
    );
  }

  const capabilities = result.capabilities
    ? Object.entries(result.capabilities)
        .filter(([, supported]) => supported)
        .map(([name]) => ({ ptz: 'PTZ', imaging: 'Imaging', analytics: 'Analytics', replay: 'Recordings on the camera', deviceIo: 'Inputs and relays' })[name] ?? name)
    : [];

  return (
    <div role="status" className="alert alert-ok stack">
      <strong>Connected.</strong>
      {result.device && (
        <span>
          {[result.device.manufacturer, result.device.model].filter(Boolean).join(' ')}
          {result.device.firmwareVersion && `, firmware ${result.device.firmwareVersion}`}
        </span>
      )}
      {result.stream && <span>Stream: {[result.stream.codec, result.stream.audio].filter(Boolean).join(' + ')}</span>}
      {capabilities.length > 0 && <span>Supports: {capabilities.join(', ')}</span>}
      {result.profiles && result.profiles.length > 0 && (
        <table className="compact">
          <caption>Media profiles</caption>
          <thead>
            <tr>
              <th>Record</th>
              <th>Profile</th>
              <th>Video</th>
              <th>Features</th>
            </tr>
          </thead>
          <tbody>
            {result.profiles.map((profile, index) => (
              <tr key={profile.token}>
                <td>
                  <input
                    type="radio"
                    name="profile"
                    aria-label={`Record profile ${profile.name ?? profile.token}`}
                    checked={form.profileToken ? form.profileToken === profile.token : index === 0}
                    onChange={() => set('profileToken', profile.token)}
                  />
                </td>
                <td>
                  {profile.name ?? profile.token} <span className="muted small">{profile.token}</span>
                </td>
                <td>
                  {[profile.encoding, profile.width && profile.height ? `${profile.width}×${profile.height}` : null, profile.frameRate ? `${profile.frameRate} fps` : null]
                    .filter(Boolean)
                    .join(', ')}
                </td>
                <td>{[profile.ptz && 'PTZ', profile.analytics && 'Analytics'].filter(Boolean).join(', ')}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}

/** Cameras found with WS-Discovery; already configured ones are marked. */
function Discovery({ onUse }: { onUse(device: DiscoveredDevice, taken: string[]): void }) {
  const { api } = useAuth();
  const configs = useQuery({ queryKey: ['camera-configs'], queryFn: api.cameras.list });
  const find = useMutation({ mutationFn: api.cameras.discover });
  const taken = configs.data?.map((c) => c.id) ?? [];

  return (
    <div className="card stack">
      <div className="heading">
        <h2>Find cameras on the network</h2>
        <button type="button" onClick={() => find.mutate()} disabled={find.isPending}>
          {find.isPending ? 'Searching…' : 'Search'}
        </button>
      </div>
      <ErrorMessage error={find.error} />
      {find.data && find.data.length === 0 && <p className="empty">No ONVIF cameras answered. They must be on the recorder's network.</p>}
      {find.data && find.data.length > 0 && (
        <ul className="devices">
          {find.data.map((device) => (
            <li key={device.endpointReference ?? device.deviceServiceUri}>
              <div>
                <strong>{device.name || 'ONVIF device'}</strong> {device.hardware && <span className="muted">{device.hardware}</span>}
                <div className="muted small">{device.deviceServiceUri}</div>
              </div>
              {device.configuredCameraId ? (
                <span className="badge">added as {device.configuredCameraId}</span>
              ) : (
                <button type="button" onClick={() => onUse(device, taken)}>
                  Use
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
