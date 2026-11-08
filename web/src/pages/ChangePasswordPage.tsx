import { useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { PasswordForm } from '../components/PasswordForm';

/** Shown after signing in with a generated or reset password, before anything else. */
export function ChangePasswordPage() {
  const navigate = useNavigate();
  const { signOut } = useAuth();

  return (
    <main className="centered">
      <div className="card stack narrow">
        <h1>Choose a new password</h1>
        <p className="muted">Your password was set by an administrator. Choose your own to continue.</p>
        <PasswordForm onChanged={() => navigate('/cameras', { replace: true })} />
        <button type="button" className="link" onClick={signOut}>
          Sign out
        </button>
      </div>
    </main>
  );
}
