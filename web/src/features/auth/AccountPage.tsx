import { useState, type FormEvent } from 'react';
import { useMutation } from '@tanstack/react-query';
import { useApi } from '../../data/ApiContext';
import { TextField } from '../../shared/form/fields';
import { formatDate } from '../../shared/format';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useAuth, useUser } from './AuthContext';
import { PasswordForm } from './PasswordForm';
import { RecoveryCodes, TwoFactorSetup } from './TwoFactorSetup';

export default function AccountPage() {
  const user = useUser();

  return (
    <section className="stack">
      <h1>Account</h1>
      <dl className="facts">
        <dt>User name</dt>
        <dd>{user.userName}</dd>
        <dt>Role</dt>
        <dd>{user.role}</dd>
        <dt>Last sign-in</dt>
        <dd>{formatDate(user.lastLoginAt)}</dd>
      </dl>
      <h2>Change password</h2>
      <PasswordForm />
      <h2>Two-factor sign-in</h2>
      {user.twoFactor === 'On' ? <TwoFactorOn /> : <TwoFactorSetup onDone={() => undefined} />}
    </section>
  );
}

/** On: how many recovery codes are left, new ones, and turning it off (not where the organization requires it). */
function TwoFactorOn() {
  const user = useUser();
  const api = useApi();
  const { reload } = useAuth();
  const [action, setAction] = useState<'codes' | 'off' | null>(null);
  const [code, setCode] = useState('');
  const [password, setPassword] = useState('');
  const [codes, setCodes] = useState<string[] | null>(null);
  const required = user.organization.requireTwoFactor === true;
  const renew = useMutation({ mutationFn: () => api.auth.twoFactor.newRecoveryCodes(code), onSuccess: ({ recoveryCodes }) => setCodes(recoveryCodes) });
  const turnOff = useMutation({ mutationFn: () => api.auth.twoFactor.disable(password, code), onSuccess: () => void reload() });

  function choose(next: 'codes' | 'off' | null) {
    setAction(next);
    setCode('');
    setPassword('');
    renew.reset();
    turnOff.reset();
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    if (action === 'codes') renew.mutate();
    else turnOff.mutate();
  }

  if (codes) {
    return (
      <RecoveryCodes
        codes={codes}
        onDone={() => {
          setCodes(null);
          choose(null);
          void reload();
        }}
      />
    );
  }

  return (
    <div className="stack">
      <p>
        <span className="state state-recording">On</span> Signing in takes a code from your authenticator app. {user.recoveryCodesLeft ?? 0} recovery codes left.
      </p>
      {required && <p className="muted small">Your organization requires two-factor sign-in.</p>}
      {action === null ? (
        <div className="actions start">
          <button type="button" onClick={() => choose('codes')}>
            New recovery codes
          </button>
          {!required && (
            <button type="button" className="danger" onClick={() => choose('off')}>
              Turn off
            </button>
          )}
        </div>
      ) : (
        <form className="stack narrow" onSubmit={submit} aria-label={action === 'codes' ? 'New recovery codes' : 'Turn off two-factor sign-in'}>
          {action === 'off' && <TextField label="Password" type="password" autoComplete="current-password" required value={password} onChange={setPassword} />}
          <TextField label="Code from the app" inputMode="numeric" autoComplete="one-time-code" required maxLength={14} value={code} onChange={setCode} />
          <ErrorMessage error={renew.error ?? turnOff.error} />
          <div className="actions start">
            <button type="submit" className={action === 'off' ? 'danger' : 'primary'} disabled={renew.isPending || turnOff.isPending}>
              {action === 'codes' ? 'Make new codes' : 'Turn off'}
            </button>
            <button type="button" onClick={() => choose(null)}>
              Cancel
            </button>
          </div>
        </form>
      )}
    </div>
  );
}
