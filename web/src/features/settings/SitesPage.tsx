import { useState, type FormEvent } from 'react';
import type { Site } from '../../api/types';
import { useDeleteSite, useSaveSite, useSites } from '../../data/tenancy';
import { TextField } from '../../shared/form/fields';
import { browserTimeZone, TimeZoneField } from '../../shared/form/TimeZoneField';
import { useForm } from '../../shared/form/useForm';
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';

/** Places with cameras; times there are shown in the site's time zone. */
export default function SitesPage() {
  const sites = useSites();
  const remove = useDeleteSite();
  const [editing, setEditing] = useState<Site | 'new' | null>(null);
  const [deleting, setDeleting] = useState<Site | null>(null);

  return (
    <section className="stack">
      <div className="heading">
        <h1>Sites</h1>
        <button type="button" className="primary" onClick={() => setEditing('new')}>
          Add site
        </button>
      </div>
      <ErrorMessage error={sites.error ?? remove.error} />

      <div className="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Site</th>
              <th>Time zone</th>
              <th>Address</th>
              <th>
                <span className="sr-only">Actions</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {sites.data?.map((site) => (
              <tr key={site.id}>
                <td>{site.name}</td>
                <td>{site.timeZone.replace(/_/g, ' ')}</td>
                <td>{site.address ?? '—'}</td>
                <td className="row-actions">
                  <button type="button" onClick={() => setEditing(site)}>
                    Edit
                  </button>
                  <button type="button" className="danger" onClick={() => setDeleting(site)}>
                    Delete
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {editing && <SiteDialog site={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />}
      {deleting && (
        <ConfirmDialog
          title={`Delete site ${deleting.name}?`}
          confirmLabel="Delete"
          danger
          onCancel={() => setDeleting(null)}
          onConfirm={() => {
            remove.mutate(deleting.id);
            setDeleting(null);
          }}
        >
          Only a site without cameras can be deleted.
        </ConfirmDialog>
      )}
    </section>
  );
}

function SiteDialog({ site, onClose }: { site?: Site; onClose(): void }) {
  const save = useSaveSite();
  const { values, set } = useForm({ name: site?.name ?? '', timeZone: site?.timeZone ?? browserTimeZone(), address: site?.address ?? '' });

  function submit(event?: FormEvent) {
    event?.preventDefault();
    save.mutate({ id: site?.id, site: { ...values, address: values.address || null } }, { onSuccess: onClose });
  }

  return (
    <ConfirmDialog title={site ? `Edit ${site.name}` : 'Add site'} confirmLabel="Save" busy={save.isPending || !values.name.trim()} onCancel={onClose} onConfirm={() => submit()}>
      <form className="stack" onSubmit={submit} aria-label="Site">
        <TextField label="Name" required maxLength={100} value={values.name} onChange={(v) => set('name', v)} />
        <TimeZoneField value={values.timeZone} onChange={(v) => set('timeZone', v)} />
        <TextField label="Address" maxLength={200} value={values.address} onChange={(v) => set('address', v)} placeholder="Optional" />
        <ErrorMessage error={save.error} />
      </form>
    </ConfirmDialog>
  );
}
