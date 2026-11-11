import { useState, type FormEvent } from 'react';
import { Link, Navigate, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { useMutation } from '@tanstack/react-query';
import { CheckboxField, TextField } from '../../shared/form/fields';
import { useForm } from '../../shared/form/useForm';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useCloud, useSetupStatus } from '../../data/tenancy';
import { useApi, useApiClient } from '../../data/ApiContext';
import { browser } from '../../shared/browser';
import { useAuth } from './AuthContext';
import { signInSteps, type SignInStepName } from './signInSteps';

export default function LoginPage() {
  const { user, signIn, completeTwoFactor } = useAuth();
  const navigate = useNavigate();
  const from = (useLocation().state as { from?: string } | null)?.from ?? '/cameras';
  const { values, set } = useForm({ userName: '', password: '', keep: false, code: '', email: '' });
  // Back from the identity provider with a problem: shown where single sign-on is tried again.
  const ssoError = useSearchParams()[0].get('sso_error');
  const [step, setStep] = useState<SignInStepName>(ssoError ? signInSteps.move(signInSteps.initial, 'singleSignOn') : signInSteps.initial);
  const api = useApi();
  const client = useApiClient();
  const [challenge, setChallenge] = useState('');
  const done = () => navigate(from, { replace: true });
  const login = useMutation({ mutationFn: () => signIn(values.userName, values.password, values.keep) });
  const verify = useMutation({ mutationFn: () => completeTwoFactor(challenge, values.code, values.keep) });
  const singleSignOn = useMutation({
    mutationFn: async () => {
      if (!(await api.auth.singleSignOn(values.email)).available) throw new Error("Your email's organization does not sign in with single sign-on: use your password.");
      browser.leave(client.singleSignOnUrl(values.email.trim(), window.location.origin));
    },
  });
  const setup = useSetupStatus();
  const cloud = useCloud().data;

  if (user) return <Navigate to={from} replace />;
  // A recorder without users is set up first.
  if (setup.data?.required) return <Navigate to="/setup" replace />;

  function submitPassword(event: FormEvent) {
    event.preventDefault();
    login.mutate(undefined, {
      onSuccess: (next) => {
        if (!next) return done();
        setChallenge(next.challenge);
        set('code', '');
        setStep(signInSteps.move(step, 'code'));
      },
    });
  }

  function submitCode(event: FormEvent) {
    event.preventDefault();
    verify.mutate(undefined, { onSuccess: done });
  }

  function startOver() {
    verify.reset();
    set('password', '');
    setStep(signInSteps.move(step, 'password'));
  }

  function goTo(next: SignInStepName) {
    singleSignOn.reset();
    setStep(signInSteps.move(step, next));
  }

  if (step === 'singleSignOn') {
    return (
      <main className="centered">
        <form
          onSubmit={(event) => {
            event.preventDefault();
            singleSignOn.mutate();
          }}
          className="card stack narrow"
          aria-label="Single sign-on"
        >
          <h1>Single sign-on</h1>
          <p className="muted">Sign in with your organization's account (Microsoft, Google, Okta…).</p>
          {ssoError && !singleSignOn.isPending && !singleSignOn.error && (
            <p role="alert" className="alert alert-error">
              {ssoError}
            </p>
          )}
          <TextField label="Work email" type="email" autoComplete="email" required autoFocus value={values.email} onChange={(v) => set('email', v)} />
          <ErrorMessage error={singleSignOn.error} />
          <button type="submit" className="primary" disabled={singleSignOn.isPending || !values.email.trim()}>
            Continue
          </button>
          <button type="button" className="link" onClick={() => goTo('password')}>
            Use a password
          </button>
        </form>
      </main>
    );
  }

  if (step === 'code') {
    return (
      <main className="centered">
        <form onSubmit={submitCode} className="card stack narrow" aria-label="Two-factor sign-in">
          <h1>Enter your code</h1>
          <p className="muted">Open your authenticator app and enter the 6-digit code for Onvif.Open. No phone? Enter one of your recovery codes.</p>
          <TextField label="Code" inputMode="numeric" autoComplete="one-time-code" required autoFocus maxLength={14} value={values.code} onChange={(v) => set('code', v)} />
          <ErrorMessage error={verify.error} />
          <button type="submit" className="primary" disabled={verify.isPending || !values.code.trim()}>
            Sign in
          </button>
          <button type="button" className="link" onClick={startOver}>
            Start over
          </button>
        </form>
      </main>
    );
  }

  return (
    <main className="centered">
      <form onSubmit={submitPassword} className="card stack narrow" aria-label="Sign in">
        <h1>Sign in</h1>
        <TextField label={cloud?.enabled ? 'Email' : 'User name'} autoComplete="username" required autoFocus value={values.userName} onChange={(v) => set('userName', v)} />
        <TextField label="Password" type="password" autoComplete="current-password" required value={values.password} onChange={(v) => set('password', v)} />
        <CheckboxField label="Keep me signed in" checked={values.keep} onChange={(v) => set('keep', v)} />
        <ErrorMessage error={login.error} />
        <button type="submit" className="primary" disabled={login.isPending}>
          Sign in
        </button>
        <button type="button" className="link" onClick={() => goTo('singleSignOn')}>
          Use single sign-on
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
