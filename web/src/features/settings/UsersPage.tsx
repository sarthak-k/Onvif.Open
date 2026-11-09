import { useCallback, useMemo, useState } from 'react';
import type { User, UserRequest } from '../../api/types';
import { useSites } from '../../data/tenancy';
import { useDeleteUser, useUpdateUser, useUsers } from '../../data/users';
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useUser } from '../auth/AuthContext';
import { assignableRoles } from '../auth/permissions';
import { SiteAccessDialog } from './SiteAccessDialog';
import { NewUserForm } from './NewUserForm';
import { ResetPasswordDialog } from './ResetPasswordDialog';
import { UserRow } from './UserRow';

export default function UsersPage() {
  const me = useUser();
  const users = useUsers();
  const update = useUpdateUser();
  const remove = useDeleteUser();
  const [deleting, setDeleting] = useState<User | null>(null);
  const [resetting, setResetting] = useState<User | null>(null);
  const [editingSites, setEditingSites] = useState<User | null>(null);
  const sites = useSites();
  const roleOptions = useMemo(() => assignableRoles(me), [me]);

  const { mutate: change } = update;
  const onChange = useCallback((id: string, user: UserRequest) => change({ id, user }), [change]);

  return (
    <section className="stack">
      <h1>Users</h1>
      <p className="muted">
        Viewers watch cameras and recordings, operators also control cameras (PTZ, recording), administrators also manage cameras, sites, users and
        plugins, and owners also manage owners and the organization. Anyone can be limited to some sites.
      </p>
      <ErrorMessage error={users.error ?? update.error ?? remove.error} />

      <div className="table-scroll">
        <table>
          <thead>
            <tr>
              <th>User name</th>
              <th>Role</th>
              <th>Sites</th>
              <th>Enabled</th>
              <th>Last sign-in</th>
              <th>
                <span className="sr-only">Actions</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {users.data?.map((user) => (
              <UserRow
                key={user.id}
                user={user}
                isMe={user.id === me.id}
                roleOptions={roleOptions}
                sites={sites.data ?? []}
                onEditSites={setEditingSites}
                onChange={onChange}
                onResetPassword={setResetting}
                onDelete={setDeleting}
              />
            ))}
          </tbody>
        </table>
      </div>

      <NewUserForm />

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
      {editingSites && <SiteAccessDialog user={editingSites} sites={sites.data ?? []} onClose={() => setEditingSites(null)} />}
    </section>
  );
}
