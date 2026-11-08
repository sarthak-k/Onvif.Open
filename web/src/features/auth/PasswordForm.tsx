import type { FormEvent } from 'react';
import { useMutation } from '@tanstack/react-query';
import { TextField } from '../../shared/form/fields';
import { useForm } from '../../shared/form/useForm';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useAuth } from './AuthContext';

const empty = { current: '', next: '', repeat: '' };

/** Changes the signed-in user's password; the recorder checks the rules (length, not the user name, ...). */
export function PasswordForm({ onChanged }: { onChanged?(): void }) {
  const { changePassword } = useAuth();
  const { values, set, setValues } = useForm(empty);
  const change = useMutation({
    mutationFn: async () => {
      if (values.next !== values.repeat) throw new Error('The new passwords do not match.');
      await changePassword(values.current, values.next);
    },
    onSuccess: () => {
      setValues(empty);
      onChanged?.();
    },
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    change.mutate();
  }

  return (
    <form onSubmit={submit} className="stack narrow">
      <TextField label="Current password" type="password" autoComplete="current-password" required value={values.current} onChange={(v) => set('current', v)} />
      <TextField label="New password" type="password" autoComplete="new-password" required value={values.next} onChange={(v) => set('next', v)} />
      <TextField label="Repeat new password" type="password" autoComplete="new-password" required value={values.repeat} onChange={(v) => set('repeat', v)} />
      <ErrorMessage error={change.error} />
      {change.isSuccess && (
        <p role="status" className="alert alert-ok">
          Password changed. Other sessions are signed out.
        </p>
      )}
      <div className="actions">
        <button type="submit" className="primary" disabled={change.isPending}>
          Change password
        </button>
      </div>
    </form>
  );
}
