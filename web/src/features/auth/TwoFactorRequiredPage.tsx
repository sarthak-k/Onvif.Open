import { useNavigate } from 'react-router-dom';
import { useAuth } from './AuthContext';
import { TwoFactorSetup } from './TwoFactorSetup';

/** Shown where the organization requires two-factor sign-in and the user has not set it up, before anything else. */
export default function TwoFactorRequiredPage() {
  const navigate = useNavigate();
  const { signOut } = useAuth();

  return (
    <main className="centered">
      <div className="card stack narrow">
        <h1>Set up two-factor sign-in</h1>
        <p className="muted">Your organization requires a code from your phone as well as your password.</p>
        <TwoFactorSetup onDone={() => navigate('/home', { replace: true })} />
        <button type="button" className="link" onClick={signOut}>
          Sign out
        </button>
      </div>
    </main>
  );
}
