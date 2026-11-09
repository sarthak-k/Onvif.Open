import { useState } from 'react';
import { useAuditLog } from '../../data/audit';
import { useUsers } from '../../data/users';
import { SelectField } from '../../shared/form/fields';
import { formatDate } from '../../shared/format';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { auditAreas, describeAction, describeDetails } from './auditActions';

/** Who did what and when: changes, sign-ins and who watched or exported video. */
export default function AuditPage() {
  const [area, setArea] = useState('');
  const [userId, setUserId] = useState('');
  const users = useUsers();
  const log = useAuditLog({ action: area || undefined, userId: userId || undefined });
  const entries = log.data?.pages.flat() ?? [];

  const userOptions = [{ value: '', label: 'Everyone' }, ...(users.data ?? []).map((user) => ({ value: user.id, label: user.userName }))];

  return (
    <section className="stack">
      <div className="heading">
        <h1>Audit log</h1>
      </div>
      <p className="muted">Changes, sign-ins and who watched or exported video, newest first.</p>

      <div className="toolbar">
        <SelectField label="Show" value={area} options={auditAreas} onChange={setArea} />
        <SelectField label="User" value={userId} options={userOptions} onChange={setUserId} />
      </div>
      <ErrorMessage error={log.error} />

      <div className="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Time</th>
              <th>User</th>
              <th>Action</th>
              <th>On</th>
              <th>Details</th>
              <th>Address</th>
            </tr>
          </thead>
          <tbody>
            {entries.map((entry) => (
              <tr key={entry.id}>
                <td>{formatDate(entry.time)}</td>
                <td>{entry.userName ?? '—'}</td>
                <td title={entry.action}>{describeAction(entry.action)}</td>
                <td>{entry.targetName ?? entry.targetId ?? '—'}</td>
                <td className="small">{describeDetails(entry.details)}</td>
                <td className="small muted">{entry.ipAddress ?? ''}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {log.isSuccess && entries.length === 0 && <p className="muted">Nothing recorded yet.</p>}
      {log.hasNextPage && (
        <div className="actions">
          <button type="button" onClick={() => log.fetchNextPage()} disabled={log.isFetchingNextPage}>
            {log.isFetchingNextPage ? 'Loading…' : 'Load more'}
          </button>
        </div>
      )}
    </section>
  );
}
