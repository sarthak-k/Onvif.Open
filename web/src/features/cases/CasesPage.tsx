import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import type { CaseStatus } from '../../api/types';
import { useCases, useCreateCase } from '../../data/cases';
import { SelectField, TextAreaField, TextField } from '../../shared/form/fields';
import { formatDate } from '../../shared/format';
import { useForm } from '../../shared/form/useForm';
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useUser } from '../auth/AuthContext';
import { can } from '../auth/permissions';

const statusOptions = [
  { value: 'Open' as const, label: 'Open cases' },
  { value: 'Closed' as const, label: 'Closed cases' },
  { value: '' as const, label: 'All cases' },
];

/** Incidents being looked into, each with the clips of video that show it. */
export default function CasesPage() {
  const [status, setStatus] = useState<CaseStatus | ''>('Open');
  const cases = useCases(status || undefined);
  const [creating, setCreating] = useState(false);
  const canEdit = can(useUser(), 'cameras.control');

  return (
    <section className="stack">
      <div className="heading">
        <h1>Cases</h1>
        {canEdit && (
          <button type="button" className="primary" onClick={() => setCreating(true)}>
            New case
          </button>
        )}
      </div>
      <p className="muted">Clips saved from Investigate, with notes. The video of an open case is kept until the case is closed.</p>
      <div className="toolbar">
        <SelectField<CaseStatus | ''> label="Show" value={status} options={statusOptions} onChange={setStatus} />
      </div>
      <ErrorMessage error={cases.error} />

      {cases.isSuccess && cases.data.length === 0 ? (
        <p className="muted">No cases here. Save a clip from Investigate to start one.</p>
      ) : (
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Case</th>
                <th>Status</th>
                <th>Clips</th>
                <th>Last change</th>
                <th>Opened by</th>
              </tr>
            </thead>
            <tbody>
              {cases.data?.map((c) => (
                <tr key={c.id}>
                  <td>
                    <Link to={`/cases/${c.id}`}>{c.name}</Link>
                  </td>
                  <td>{c.status}</td>
                  <td>{c.clipCount ?? 0}</td>
                  <td>{formatDate(c.updatedAt)}</td>
                  <td>{c.createdBy ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {creating && <NewCaseDialog onClose={() => setCreating(false)} />}
    </section>
  );
}

function NewCaseDialog({ onClose }: { onClose(): void }) {
  const create = useCreateCase();
  const navigate = useNavigate();
  const { values, set } = useForm({ name: '', description: '' });

  function submit(event?: FormEvent) {
    event?.preventDefault();
    create.mutate({ name: values.name.trim(), description: values.description.trim() || null }, { onSuccess: (c) => navigate(`/cases/${c.id}`) });
  }

  return (
    <ConfirmDialog title="New case" confirmLabel="Create" busy={create.isPending || !values.name.trim()} onCancel={onClose} onConfirm={() => submit()}>
      <form className="stack" aria-label="New case" onSubmit={submit}>
        <TextField label="Name" required maxLength={200} placeholder="Break-in at the back door" value={values.name} onChange={(v) => set('name', v)} />
        <TextAreaField label="Description" rows={3} value={values.description} onChange={(v) => set('description', v)} />
        <ErrorMessage error={create.error} />
      </form>
    </ConfirmDialog>
  );
}
