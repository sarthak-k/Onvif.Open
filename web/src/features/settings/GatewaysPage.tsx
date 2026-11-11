import { useState, type FormEvent } from 'react';
import type { Gateway } from '../../api/types';
import { useApi } from '../../data/ApiContext';
import { useCreateCloudGateway, useGateways, usePairGateway, useRemoveGateway, useStartGateway, useUpdateGateway } from '../../data/gateways';
import { useCloud } from '../../data/tenancy';
import { CheckboxField, NumberField, RadioGroup, Section, SelectField, TextAreaField, TextField } from '../../shared/form/fields';
import { formatDate } from '../../shared/format';
import { useForm } from '../../shared/form/useForm';
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useUser } from '../auth/AuthContext';
import { can } from '../auth/permissions';
import { backupDestinations, backupSettingHints, describeBackup, describeCameras, describeStorage, toFormValues, toRequest } from './gatewayForm';

const locationLabels: Record<Gateway['location'], string> = { Local: 'On a local machine', Cloud: 'In the cloud' };
const statusClass: Record<Gateway['status'], string> = { Online: 'state-recording', Offline: 'state-reconnecting', Waiting: 'state-stopped' };
const hostingLabels: Record<NonNullable<Gateway['hosting']>['state'], string> = { Starting: 'Starting…', Running: 'Running', Failed: 'Could not start' };

/** What records the cameras, whether it is healthy, and its settings: storage, cloud backup, discovery. */
export default function GatewaysPage() {
  const user = useUser();
  const gateways = useGateways();
  const [editing, setEditing] = useState<Gateway | null>(null);
  const [adding, setAdding] = useState(false);
  const [removing, setRemoving] = useState<Gateway | null>(null);
  const remove = useRemoveGateway();
  const start = useStartGateway();
  const manage = can(user, 'settings.manage');

  return (
    <section className="stack">
      <div className="heading">
        <h1>Gateways</h1>
        {manage && (
          <button type="button" className="primary" onClick={() => setAdding(true)}>
            Add gateway
          </button>
        )}
      </div>
      <p className="muted">A gateway records the cameras of a site and keeps their video. This server is your first gateway.</p>
      <ErrorMessage error={gateways.error ?? remove.error ?? start.error} />

      {gateways.data?.map((gateway) => (
        <article key={gateway.id} className="card stack" aria-label={gateway.name}>
          <div className="heading">
            <h2>
              {gateway.name}
              {gateway.embedded && <span className="badge">this server</span>}
            </h2>
            {manage && (
              <div className="actions">
                {gateway.embedded && (
                  <button type="button" onClick={() => setEditing(gateway)}>
                    Edit
                  </button>
                )}
                {gateway.hosting && gateway.hosting.state !== 'Starting' && (
                  <button type="button" onClick={() => start.mutate(gateway.id)} disabled={start.isPending}>
                    Start again
                  </button>
                )}
                {!gateway.embedded && (
                  <button type="button" className="danger" onClick={() => setRemoving(gateway)}>
                    Remove
                  </button>
                )}
              </div>
            )}
          </div>
          {gateway.restartRequired && <p className="alert alert-error">Restart the recorder to apply the new backup settings.</p>}
          {gateway.hosting?.state === 'Failed' && gateway.hosting.error && <p className="alert alert-error">{gateway.hosting.error}</p>}
          <dl className="facts">
            <dt>Status</dt>
            <dd>
              <span className={`state ${statusClass[gateway.status]}`}>{gateway.status}</span>
              {gateway.lastSeenAt && <span className="muted small"> · last report {formatDate(gateway.lastSeenAt)}</span>}
            </dd>
            <dt>Runs</dt>
            <dd>
              {gateway.hosting ? 'In the cloud, run for you' : locationLabels[gateway.location]}
              {gateway.hosting && <span className="muted small"> · {hostingLabels[gateway.hosting.state]}</span>}
            </dd>
            <dt>Cameras</dt>
            <dd>{describeCameras(gateway)}</dd>
            <dt>Storage</dt>
            <dd>{describeStorage(gateway)}</dd>
            <dt>Cloud backup</dt>
            <dd>{describeBackup(gateway)}</dd>
            <dt>Version</dt>
            <dd>{gateway.version ?? '—'}</dd>
          </dl>
        </article>
      ))}

      {editing && <GatewayDialog gateway={editing} onClose={() => setEditing(null)} />}
      {adding && <AddGatewayDialog onClose={() => setAdding(false)} />}
      {removing && (
        <ConfirmDialog
          title={`Remove ${removing.name}?`}
          confirmLabel="Remove"
          danger
          onCancel={() => setRemoving(null)}
          onConfirm={() => {
            remove.mutate(removing.id);
            setRemoving(null);
          }}
        >
          {removing.hosting ? 'It is stopped and stops recording.' : 'It stops reporting here. To use it again, restart it and enter its new code.'}
        </ConfirmDialog>
      )}
    </section>
  );
}

function GatewayDialog({ gateway, onClose }: { gateway: Gateway; onClose(): void }) {
  const save = useUpdateGateway();
  const { values, set } = useForm(toFormValues(gateway));
  const provider = values.destination === 'other' ? values.otherProvider.trim() : values.destination;
  const storedSettings = provider === (gateway.backup?.provider ?? '') ? (gateway.backup?.settings ?? []) : [];

  function submit(event?: FormEvent) {
    event?.preventDefault();
    save.mutate({ id: gateway.id, gateway: toRequest(values, gateway) }, { onSuccess: onClose });
  }

  return (
    <ConfirmDialog title={`Edit ${gateway.name}`} confirmLabel="Save" busy={save.isPending || !values.name.trim()} onCancel={onClose} onConfirm={() => submit()}>
      <form className="stack" onSubmit={submit} aria-label="Gateway">
        <TextField label="Name" required maxLength={100} value={values.name} onChange={(v) => set('name', v)} />

        <Section title="Storage">
          <p className="muted small">The oldest recordings go first when a limit is reached. Empty: no limit.</p>
          <div className="row">
            <NumberField label="Keep free (GB)" value={values.minFreeSpaceGB} onChange={(v) => set('minFreeSpaceGB', v)} />
            <NumberField label="Per camera (GB)" value={values.maxSizePerCameraGB} onChange={(v) => set('maxSizePerCameraGB', v)} />
            <NumberField label="In total (GB)" value={values.maxTotalSizeGB} onChange={(v) => set('maxTotalSizeGB', v)} />
          </div>
        </Section>

        <Section title="Cloud backup">
          <SelectField label="Back up recordings to" value={values.destination} options={backupDestinations} onChange={(v) => set('destination', v)} />
          {values.destination === 'other' && <TextField label="Plugin folder" value={values.otherProvider} onChange={(v) => set('otherProvider', v)} />}
          {provider && (
            <>
              <TextAreaField
                label="Settings (one Name=value per line)"
                rows={4}
                value={values.backupSettings}
                placeholder={storedSettings.length > 0 ? `Stored: ${storedSettings.join(', ')}. Leave empty to keep them.` : backupSettingHints[provider]}
                onChange={(v) => set('backupSettings', v)}
              />
              <div className="row">
                <TextField label="Folder prefix" value={values.prefix} onChange={(v) => set('prefix', v)} />
                <NumberField label="Keep the gateway's copy (hours)" step="1" value={values.keepLocalCopyHours} onChange={(v) => set('keepLocalCopyHours', v)} />
              </div>
            </>
          )}
          <p className="muted small">A changed backup applies when the recorder restarts. Settings are stored encrypted and never shown again.</p>
        </Section>

        <Section title="Finding cameras">
          <CheckboxField label="Search the local network (multicast)" checked={values.multicast} onChange={(v) => set('multicast', v)} />
          <TextAreaField label="Also ask these addresses (one per line)" rows={3} placeholder="10.0.5.20" value={values.addresses} onChange={(v) => set('addresses', v)} />
        </Section>
        <ErrorMessage error={save.error} />
      </form>
    </ConfirmDialog>
  );
}

const placeOptions = [
  { value: 'local' as const, label: 'On a local machine' },
  { value: 'cloud' as const, label: 'In the cloud' },
];

/**
 * Pairs a gateway installed on a machine at a site (it shows a 6-digit code, entered here), or, where the cloud offers
 * it, starts one in the cloud for cameras on the internet.
 */
function AddGatewayDialog({ onClose }: { onClose(): void }) {
  const api = useApi();
  const pair = usePairGateway();
  const createCloud = useCreateCloudGateway();
  const hosted = useCloud().data?.cloudGateways ?? false;
  const { values, set } = useForm({ place: 'local' as 'local' | 'cloud', code: '', name: '' });
  const digits = values.code.replace(/\D/g, '');
  const local = values.place === 'local';

  function submit(event?: FormEvent) {
    event?.preventDefault();
    if (local && digits.length === 6) {
      pair.mutate({ code: digits, name: values.name.trim() || null }, { onSuccess: onClose });
    } else if (!local && hosted) {
      createCloud.mutate(values.name.trim() || null, { onSuccess: onClose });
    }
  }

  const busy = local ? digits.length !== 6 || pair.isPending : !hosted || createCloud.isPending;
  return (
    <ConfirmDialog title="Add gateway" confirmLabel={local ? 'Pair' : 'Start gateway'} busy={busy} onCancel={onClose} onConfirm={() => submit()}>
      <form className="stack" onSubmit={submit} aria-label="Add gateway">
        <RadioGroup legend="Where does it run?" value={values.place} options={placeOptions} onChange={(v) => set('place', v)} />
        {local ? (
          <>
            <ol className="small">
              <li>Install Onvif.Open on a computer at the site, on the same network as its cameras.</li>
              <li>
                Start it with <code>ControlPlane:Url</code> set to <code>{api.gateways.controlPlaneUrl()}</code>. It only connects out: nothing to open on the
                site's firewall.
              </li>
              <li>Enter the 6-digit code it shows in its log.</li>
            </ol>
            <TextField label="Pairing code" inputMode="numeric" autoComplete="off" placeholder="123 456" maxLength={7} value={values.code} onChange={(v) => set('code', v)} />
            <TextField label="Name (optional)" maxLength={100} placeholder="The name the gateway gives" value={values.name} onChange={(v) => set('name', v)} />
          </>
        ) : hosted ? (
          <>
            <p className="small">
              We run a gateway for you, for cameras reachable on the internet (a public address or a port forward). Cameras on a site's own network need a gateway
              there.
            </p>
            <TextField label="Name (optional)" maxLength={100} placeholder="Cloud gateway" value={values.name} onChange={(v) => set('name', v)} />
          </>
        ) : (
          <p className="muted">Gateways run for you in the cloud come with cloud hosting. Until then, add one on a local machine.</p>
        )}
        <ErrorMessage error={pair.error ?? createCloud.error} />
      </form>
    </ConfirmDialog>
  );
}
