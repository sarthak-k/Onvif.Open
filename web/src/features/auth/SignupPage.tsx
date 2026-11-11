import type { FormEvent } from 'react';
import { Link, Navigate } from 'react-router-dom';
import { useMutation } from '@tanstack/react-query';
import type { Region } from '../../api/types';
import { useCloud } from '../../data/tenancy';
import { RadioGroup, TextField } from '../../shared/form/fields';
import { browserTimeZone, TimeZoneField } from '../../shared/form/TimeZoneField';
import { useForm } from '../../shared/form/useForm';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useAuth } from './AuthContext';

/**
 * Cloud sign-up: the organization, its first site and the owner's account, kept in this deployment's region. Another
 * region is another deployment, so choosing one goes there. Nothing is created until the emailed link is opened.
 */
export default function SignupPage() {
  const cloud = useCloud();
  const { user, startSignup } = useAuth();
  const { values, set } = useForm({
    region: null as Region | null,
    organizationName: '',
    siteName: '',
    timeZone: browserTimeZone(),
    email: '',
    password: '',
    repeat: '',
  });

  const signup = useMutation({
    mutationFn: async () => {
      if (values.password !== values.repeat) throw new Error('The passwords do not match.');
      const { region: _region, repeat: _repeat, ...request } = values;
      await startSignup(request);
    },
  });

  if (user) return <Navigate to="/home" replace />;
  if (cloud.isLoading) return <p className="loading">Loading…</p>;
  if (cloud.data && !cloud.data.signup) return <Navigate to="/login" replace />;

  const here = cloud.data?.region ?? null;
  const region = values.region ?? here;
  const elsewhere = region !== here ? cloud.data?.regions?.find((r) => r.id === region) : undefined;

  function submit(event: FormEvent) {
    event.preventDefault();
    signup.mutate();
  }

  if (signup.isSuccess) {
    return (
      <main className="centered">
        <section className="card stack narrow" aria-label="Check your email">
          <h1>Check your email</h1>
          <p>
            We sent a link to <strong>{values.email}</strong>. Open it within a day to finish setting up your organization.
          </p>
          <p className="muted small">Nothing arrived? Look in spam, or sign up again.</p>
        </section>
      </main>
    );
  }

  return (
    <main className="centered">
      <form onSubmit={submit} className="card stack setup" aria-label="Create an account">
        <div>
          <h1>Create an account</h1>
          <p className="muted">
            Already have one? <Link to="/login">Sign in</Link>
          </p>
        </div>
        <ErrorMessage error={cloud.error} />

        <fieldset className="stack plain">
          <legend>Your organization</legend>
          {(cloud.data?.regions?.length ?? 0) > 1 && region && (
            <RadioGroup legend="Where your data is kept" value={region} options={(cloud.data?.regions ?? []).map((r) => ({ value: r.id, label: r.name }))} onChange={(v) => set('region', v)} />
          )}
          {elsewhere ? (
            <p>
              Organizations in {elsewhere.name} sign up there: <a href={`${elsewhere.webUrl}/signup`}>continue in {elsewhere.name}</a>.
            </p>
          ) : (
            <>
              <p className="muted small">The region cannot be changed later.</p>
              <TextField label="Organization name" required maxLength={100} value={values.organizationName} onChange={(v) => set('organizationName', v)} placeholder="Riverside Shops" />
            </>
          )}
        </fieldset>

        {!elsewhere && (
          <>
            <fieldset className="stack plain">
              <legend>Your first site</legend>
              <div className="row">
                <TextField label="Site name" required maxLength={100} value={values.siteName} onChange={(v) => set('siteName', v)} placeholder="Head office" />
                <TimeZoneField value={values.timeZone} onChange={(v) => set('timeZone', v)} />
              </div>
            </fieldset>

            <fieldset className="stack plain">
              <legend>Your account (owner)</legend>
              <TextField label="Email" type="email" required maxLength={64} autoComplete="email" value={values.email} onChange={(v) => set('email', v)} />
              <div className="row">
                <TextField label="Password" type="password" required autoComplete="new-password" value={values.password} onChange={(v) => set('password', v)} />
                <TextField label="Repeat password" type="password" required autoComplete="new-password" value={values.repeat} onChange={(v) => set('repeat', v)} />
              </div>
            </fieldset>

            <ErrorMessage error={signup.error} />
            <button type="submit" className="primary" disabled={signup.isPending}>
              Create account
            </button>
          </>
        )}
      </form>
    </main>
  );
}
