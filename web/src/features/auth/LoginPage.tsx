import type { FormEvent } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { useMutation } from '@tanstack/react-query';
import { CheckboxField, TextField } from '../../shared/form/fields';
import { useForm } from '../../shared/form/useForm';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useAuth } from './AuthContext';

export default function LoginPage() {
  const { user, signIn } = useAuth();
  const navigate = useNavigate();
  const from = (useLocation().state as { from?: string } | null)?.from ?? '/cameras';
  const { values, set } = useForm({ userName: '', password: '', keep: false });
  const login = useMutation({ mutationFn: () => signIn(values.userName, values.password, values.keep) });

  if (user) return <Navigate to={from} replace />;

  function submit(event: FormEvent) {
    event.preventDefault();
    login.mutate(undefined, { onSuccess: () => navigate(from, { replace: true }) });
  }

  return (
    <main className="centered">
      <form onSubmit={submit} className="card stack narrow" aria-label="Sign in">
        <h1>Sign in</h1>
        <TextField label="User name" autoComplete="username" required autoFocus value={values.userName} onChange={(v) => set('userName', v)} />
        <TextField label="Password" type="password" autoComplete="current-password" required value={values.password} onChange={(v) => set('password', v)} />
        <CheckboxField label="Keep me signed in" checked={values.keep} onChange={(v) => set('keep', v)} />
        <ErrorMessage error={login.error} />
        <button type="submit" className="primary" disabled={login.isPending}>
          Sign in
        </button>
      </form>
    </main>
  );
}
