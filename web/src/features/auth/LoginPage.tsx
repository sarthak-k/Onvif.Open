import type { FormEvent } from 'react';
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom';
import { useMutation } from '@tanstack/react-query';
import { CheckboxField, TextField } from '../../shared/form/fields';
import { useForm } from '../../shared/form/useForm';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useCloud, useSetupStatus } from '../../data/tenancy';
import { useAuth } from './AuthContext';

export default function LoginPage() {
  const { user, signIn } = useAuth();
  const navigate = useNavigate();
  const from = (useLocation().state as { from?: string } | null)?.from ?? '/cameras';
  const { values, set } = useForm({ userName: '', password: '', keep: false });
  const login = useMutation({ mutationFn: () => signIn(values.userName, values.password, values.keep) });
  const setup = useSetupStatus();
  const cloud = useCloud().data;

  if (user) return <Navigate to={from} replace />;
  // A recorder without users is set up first.
  if (setup.data?.required) return <Navigate to="/setup" replace />;

  function submit(event: FormEvent) {
    event.preventDefault();
    login.mutate(undefined, { onSuccess: () => navigate(from, { replace: true }) });
  }

  return (
    <main className="centered">
      <form onSubmit={submit} className="card stack narrow" aria-label="Sign in">
        <h1>Sign in</h1>
        <TextField label={cloud?.enabled ? 'Email' : 'User name'} autoComplete="username" required autoFocus value={values.userName} onChange={(v) => set('userName', v)} />
        <TextField label="Password" type="password" autoComplete="current-password" required value={values.password} onChange={(v) => set('password', v)} />
        <CheckboxField label="Keep me signed in" checked={values.keep} onChange={(v) => set('keep', v)} />
        <ErrorMessage error={login.error} />
        <button type="submit" className="primary" disabled={login.isPending}>
          Sign in
        </button>
        {cloud?.signup && (
          <p className="muted">
            New here? <Link to="/signup">Create an account</Link>
          </p>
        )}
      </form>
    </main>
  );
}
