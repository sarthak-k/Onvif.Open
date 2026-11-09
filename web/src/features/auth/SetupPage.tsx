import type { FormEvent } from 'react';
import { Navigate } from 'react-router-dom';
import { useMutation } from '@tanstack/react-query';
import type { Region } from '../../api/types';
import { useSetupStatus } from '../../data/tenancy';
import { RadioGroup, TextField } from '../../shared/form/fields';
import { browserTimeZone, TimeZoneField } from '../../shared/form/TimeZoneField';
import { useForm } from '../../shared/form/useForm';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useAuth } from './AuthContext';

/** First run: the organization, its first site and the owner's account, on one page. */
export default function SetupPage() {
  const status = useSetupStatus();
  const { user, completeSetup } = useAuth();
  const { values, set } = useForm({
    organizationName: '',
    region: 'us' as Region,
    siteName: '',
    timeZone: browserTimeZone(),
    userName: '',
    password: '',
    repeat: '',
    setupToken: '',
  });

  const setup = useMutation({
    mutationFn: async () => {
      if (values.password !== values.repeat) throw new Error('The passwords do not match.');
      const { repeat: _repeat, setupToken, ...request } = values;
      await completeSetup({ ...request, setupToken: setupToken || undefined });
    },
  });

  // Just set up: on to the first camera. Already signed in otherwise: nothing to set up.
  if (user) return <Navigate to={setup.isPending || setup.isSuccess ? '/cameras/new' : '/live'} replace />;
  if (status.isLoading) return <p className="loading">Loading…</p>;
  if (status.data && !status.data.required) return <Navigate to="/login" replace />;

  function submit(event: FormEvent) {
    event.preventDefault();
    setup.mutate();
  }

  return (
    <main className="centered">
      <form onSubmit={submit} className="card stack setup" aria-label="Set up">
        <div>
          <h1>Set up Onvif.Open</h1>
          <p className="muted">Three things and you can add your first camera.</p>
        </div>
        <ErrorMessage error={status.error} />

        <fieldset className="stack plain">
          <legend>Your organization</legend>
          <TextField label="Organization name" required maxLength={100} value={values.organizationName} onChange={(v) => set('organizationName', v)} placeholder="Riverside Shops" />
          <RadioGroup
            legend="Where your data is kept"
            value={values.region}
            options={(status.data?.regions ?? []).map((r) => ({ value: r.id, label: r.name }))}
            onChange={(v) => set('region', v)}
          />
          <p className="muted small">The region cannot be changed later.</p>
        </fieldset>

        <fieldset className="stack plain">
          <legend>Your first site</legend>
          <div className="row">
            <TextField label="Site name" required maxLength={100} value={values.siteName} onChange={(v) => set('siteName', v)} placeholder="Head office" />
            <TimeZoneField value={values.timeZone} onChange={(v) => set('timeZone', v)} />
          </div>
        </fieldset>

        <fieldset className="stack plain">
          <legend>Your account (owner)</legend>
          <TextField label="User name" required autoComplete="username" value={values.userName} onChange={(v) => set('userName', v)} />
          <div className="row">
            <TextField label="Password" type="password" required autoComplete="new-password" value={values.password} onChange={(v) => set('password', v)} />
            <TextField label="Repeat password" type="password" required autoComplete="new-password" value={values.repeat} onChange={(v) => set('repeat', v)} />
          </div>
          {status.data?.tokenRequired && (
            <TextField label="Setup token" required value={values.setupToken} onChange={(v) => set('setupToken', v)} placeholder="From the recorder's configuration" />
          )}
        </fieldset>

        <ErrorMessage error={setup.error} />
        <button type="submit" className="primary" disabled={setup.isPending}>
          Create organization
        </button>
      </form>
    </main>
  );
}
