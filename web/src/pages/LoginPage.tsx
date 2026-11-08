import { useState, type FormEvent } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { ErrorMessage } from '../components/ErrorMessage';

export function LoginPage() {
  const { user, signIn } = useAuth();
  const navigate = useNavigate();
  const from = (useLocation().state as { from?: string } | null)?.from ?? '/cameras';
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [keep, setKeep] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);

  if (user) return <Navigate to={from} replace />;

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await signIn(userName, password, keep);
      navigate(from, { replace: true });
    } catch (e) {
      setError(e);
      setBusy(false);
    }
  }

  return (
    <main className="centered">
      <form onSubmit={submit} className="card stack narrow" aria-label="Sign in">
        <h1>Sign in</h1>
        <label>
          User name
          <input autoComplete="username" required autoFocus value={userName} onChange={(e) => setUserName(e.target.value)} />
        </label>
        <label>
          Password
          <input type="password" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} />
        </label>
        <label className="check">
          <input type="checkbox" checked={keep} onChange={(e) => setKeep(e.target.checked)} />
          Keep me signed in
        </label>
        <ErrorMessage error={error} />
        <button type="submit" className="primary" disabled={busy}>
          Sign in
        </button>
      </form>
    </main>
  );
}
