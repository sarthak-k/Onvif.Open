import { useState, type FormEvent } from 'react';
import { useAuth } from '../auth/AuthContext';
import { ErrorMessage } from './ErrorMessage';

/** Changes the signed-in user's password; the recorder checks the rules (length, not the user name, ...). */
export function PasswordForm({ onChanged }: { onChanged?(): void }) {
  const { changePassword } = useAuth();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [repeat, setRepeat] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setDone(false);
    if (next !== repeat) {
      setError(new Error('The new passwords do not match.'));
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await changePassword(current, next);
      setCurrent('');
      setNext('');
      setRepeat('');
      setDone(true);
      onChanged?.();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="stack narrow">
      <label>
        Current password
        <input type="password" autoComplete="current-password" required value={current} onChange={(e) => setCurrent(e.target.value)} />
      </label>
      <label>
        New password
        <input type="password" autoComplete="new-password" required value={next} onChange={(e) => setNext(e.target.value)} />
      </label>
      <label>
        Repeat new password
        <input type="password" autoComplete="new-password" required value={repeat} onChange={(e) => setRepeat(e.target.value)} />
      </label>
      <ErrorMessage error={error} />
      {done && <p role="status" className="alert alert-ok">Password changed. Other sessions are signed out.</p>}
      <div className="actions">
        <button type="submit" className="primary" disabled={busy}>
          Change password
        </button>
      </div>
    </form>
  );
}
