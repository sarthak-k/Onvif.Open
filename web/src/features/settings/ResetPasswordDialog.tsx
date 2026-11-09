import { useState } from 'react';
import type { User } from '../../api/types';
import { useUpdateUser } from '../../data/users';
import { TextField } from '../../shared/form/fields';
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';

export function ResetPasswordDialog({ user, onClose }: { user: User; onClose(): void }) {
  const reset = useUpdateUser();
  const [password, setPassword] = useState('');

  return (
    <ConfirmDialog
      title={`Reset the password of ${user.userName}`}
      confirmLabel="Reset password"
      busy={reset.isPending || !password}
      onCancel={onClose}
      onConfirm={() => reset.mutate({ id: user.id, user: { password } }, { onSuccess: onClose })}
    >
      <div className="stack">
        <TextField label="New password" type="password" autoComplete="new-password" value={password} onChange={setPassword} />
        <p className="muted">The user is signed out and must choose a new password at the next sign-in.</p>
        <ErrorMessage error={reset.error} />
      </div>
    </ConfirmDialog>
  );
}
