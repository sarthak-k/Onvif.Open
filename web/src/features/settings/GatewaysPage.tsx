import { useState, type FormEvent } from 'react';
import type { Gateway } from '../../api/types';
import { useGateways, useUpdateGateway } from '../../data/gateways';
import { CheckboxField, NumberField, Section, SelectField, TextAreaField, TextField } from '../../shared/form/fields';
import { formatDate } from '../../shared/format';
import { useForm } from '../../shared/form/useForm';
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useUser } from '../auth/AuthContext';
import { can } from '../auth/permissions';
import { backupDestinations, backupSettingHints, describeBackup, describeCameras, describeStorage, toFormValues, toRequest } from './gatewayForm';

const locationLabels: Record<Gateway['location'], string> = { Local: 'On a local machine', Cloud: 'In the cloud' };
const statusClass: Record<Gateway['status'], string> = { Online: 'state-recording', Offline: 'state-reconnecting', Waiting: 'state-stopped' };

/** What records the cameras, whether it is healthy, and its settings: storage, cloud backup, discovery. */
export default function GatewaysPage() {
  const user = useUser();
  const gateways = useGateways();
  const [editing, setEditing] = useState<Gateway | null>(null);
  const manage = can(user, 'settings.manage');

  return (
    <section className="stack">
      <div className="heading">
        <h1>Gateways</h1>
      </div>
      <p className="muted">A gateway records the cameras of a site and keeps their video. This server is your first gateway.</p>
      <ErrorMessage error={gateways.error} />

      {gateways.data?.map((gateway) => (
        <article key={gateway.id} className="card stack" aria-label={gateway.name}>
          <div className="heading">
            <h2>
              {gateway.name}
              {gateway.embedded && <span className="badge">this server</span>}
            </h2>
            {manage && (
              <button type="button" onClick={() => setEditing(gateway)}>
                Edit
              </button>
            )}
          </div>
          {gateway.restartRequired && <p className="alert alert-error">Restart the recorder to apply the new backup settings.</p>}
          <dl className="facts">
            <dt>Status</dt>
            <dd>
              <span className={`state ${statusClass[gateway.status]}`}>{gateway.status}</span>
              {gateway.lastSeenAt && <span className="muted small"> · last report {formatDate(gateway.lastSeenAt)}</span>}
            </dd>
            <dt>Runs</dt>
            <dd>{locationLabels[gateway.location]}</dd>
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
