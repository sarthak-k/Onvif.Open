import type { FormEvent } from 'react';
import { roles, type Role } from '../../api/types';
import { useCreateUser } from '../../data/users';
import { SelectField, TextField } from '../../shared/form/fields';
import { useForm } from '../../shared/form/useForm';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';

const roleOptions = roles.map((role) => ({ value: role, label: role }));
const empty = { userName: '', password: '', role: 'Viewer' as Role };

export function NewUserForm() {
  const create = useCreateUser();
  const { values, set, setValues } = useForm(empty);

  function submit(event: FormEvent) {
    event.preventDefault();
    create.mutate(values, { onSuccess: () => setValues(empty) });
  }

  return (
    <form onSubmit={submit} className="card stack" aria-label="Add user">
      <h2>Add user</h2>
      <div className="row">
        <TextField label="User name" required autoComplete="off" value={values.userName} onChange={(v) => set('userName', v)} />
        <TextField label="Initial password" type="password" required autoComplete="new-password" value={values.password} onChange={(v) => set('password', v)} />
        <SelectField label="Role" value={values.role} options={roleOptions} onChange={(v) => set('role', v)} />
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
