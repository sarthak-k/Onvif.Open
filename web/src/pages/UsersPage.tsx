import { useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { roles, type Role, type User, type UserRequest } from '../api/types';
import { useAuth, useUser } from '../auth/AuthContext';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { ErrorMessage } from '../components/ErrorMessage';
import { formatDate } from '../format';

export function UsersPage() {
  const { api } = useAuth();
  const me = useUser();
  const queries = useQueryClient();
  const users = useQuery({ queryKey: ['users'], queryFn: api.users.list });
  const refresh = () => queries.invalidateQueries({ queryKey: ['users'] });

  const update = useMutation({ mutationFn: ({ id, user }: { id: string; user: UserRequest }) => api.users.update(id, user), onSettled: refresh });
  const remove = useMutation({ mutationFn: (id: string) => api.users.remove(id), onSettled: refresh });
  const [deleting, setDeleting] = useState<User | null>(null);
  const [resetting, setResetting] = useState<User | null>(null);

  return (
    <section className="stack">
      <h1>Users</h1>
      <p className="muted">
        Viewers watch cameras and recordings, operators also control cameras (PTZ, recording), administrators also manage cameras, users and plugins.
      </p>
      <ErrorMessage error={users.error ?? update.error ?? remove.error} />

      <div className="table-scroll"><table>
        <thead>
          <tr>
            <th>User name</th>
            <th>Role</th>
            <th>Enabled</th>
            <th>Last sign-in</th>
            <th>
              <span className="sr-only">Actions</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {users.data?.map((user) => (
            <tr key={user.id}>
              <td>
                {user.userName}
                {user.id === me.id && <span className="badge">you</span>}
                {user.lockedUntil && <span className="badge warn">locked</span>}
                {user.mustChangePassword && <span className="badge">must change password</span>}
              </td>
              <td>
                <select
                  aria-label={`Role of ${user.userName}`}
                  value={user.role}
                  onChange={(e) => update.mutate({ id: user.id, user: { role: e.target.value as Role } })}
                >
                  {roles.map((role) => (
                    <option key={role}>{role}</option>
                  ))}
                </select>
              </td>
              <td>
                <input
                  type="checkbox"
                  aria-label={`${user.userName} enabled`}
                  checked={user.enabled}
                  onChange={(e) => update.mutate({ id: user.id, user: { enabled: e.target.checked } })}
                />
              </td>
              <td>{formatDate(user.lastLoginAt)}</td>
              <td className="row-actions">
                <button type="button" onClick={() => setResetting(user)}>
                  Reset password
                </button>
                <button type="button" className="danger" disabled={user.id === me.id} onClick={() => setDeleting(user)}>
                  Delete
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table></div>

      <NewUserForm onCreated={refresh} />

      {deleting && (
        <ConfirmDialog
          title={`Delete ${deleting.userName}?`}
          confirmLabel="Delete"
          danger
          onCancel={() => setDeleting(null)}
          onConfirm={() => {
            remove.mutate(deleting.id);
            setDeleting(null);
          }}
        >
          The user is signed out everywhere and cannot sign in again.
        </ConfirmDialog>
      )}
      {resetting && <ResetPasswordDialog user={resetting} onClose={() => setResetting(null)} />}
    </section>
  );
}

function NewUserForm({ onCreated }: { onCreated(): void }) {
  const { api } = useAuth();
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [role, setRole] = useState<Role>('Viewer');
  const create = useMutation({
    mutationFn: () => api.users.create({ userName, password, role }),
    onSuccess: () => {
      setUserName('');
      setPassword('');
      setRole('Viewer');
      onCreated();
    },
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    create.mutate();
  }

  return (
    <form onSubmit={submit} className="card stack" aria-label="Add user">
      <h2>Add user</h2>
      <div className="row">
        <label>
          User name
          <input required autoComplete="off" value={userName} onChange={(e) => setUserName(e.target.value)} />
        </label>
        <label>
          Initial password
          <input type="password" required autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} />
        </label>
        <label>
          Role
          <select value={role} onChange={(e) => setRole(e.target.value as Role)}>
            {roles.map((r) => (
              <option key={r}>{r}</option>
            ))}
          </select>
        </label>
      </div>
      <p className="muted">The user chooses their own password at the first sign-in.</p>
      <ErrorMessage error={create.error} />
      <div className="actions">
        <button type="submit" className="primary" disabled={create.isPending}>
          Add user
        </button>
      </div>
    </form>
  );
}

function ResetPasswordDialog({ user, onClose }: { user: User; onClose(): void }) {
  const { api } = useAuth();
  const queries = useQueryClient();
  const [password, setPassword] = useState('');
  const reset = useMutation({
    mutationFn: () => api.users.update(user.id, { password }),
    onSuccess: () => {
      void queries.invalidateQueries({ queryKey: ['users'] });
      onClose();
    },
  });

  return (
    <ConfirmDialog title={`Reset the password of ${user.userName}`} confirmLabel="Reset password" busy={reset.isPending || !password} onCancel={onClose} onConfirm={() => reset.mutate()}>
      <div className="stack">
        <label>
          New password
          <input type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} />
        </label>
        <p className="muted">The user is signed out and must choose a new password at the next sign-in.</p>
        <ErrorMessage error={reset.error} />
      </div>
    </ConfirmDialog>
  );
}
