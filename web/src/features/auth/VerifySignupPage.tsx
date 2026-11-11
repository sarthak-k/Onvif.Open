import { Link, Navigate, useSearchParams } from 'react-router-dom';
import { useMutation } from '@tanstack/react-query';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useAuth } from './AuthContext';

/**
 * The emailed sign-up link. It works once, so it is used by a click, not on load: mail scanners that open links
 * must not spend it.
 */
export default function VerifySignupPage() {
  const token = useSearchParams()[0].get('token') ?? '';
  const { user, completeSignup } = useAuth();
  const verify = useMutation({ mutationFn: () => completeSignup(token) });

  // Just signed up: on to the first camera.
  if (user) return <Navigate to={verify.isPending || verify.isSuccess ? '/cameras/add' : '/home'} replace />;

  return (
    <main className="centered">
      <section className="card stack narrow" aria-label="Finish signing up">
        <h1>Finish signing up</h1>
        <p>Your email is confirmed once you continue; your organization is ready straight after.</p>
        <ErrorMessage error={verify.error} />
        {verify.isError ? (
          <p>
            <Link to="/signup">Sign up again</Link> or <Link to="/login">sign in</Link>.
          </p>
        ) : (
          <button type="button" className="primary" disabled={!token || verify.isPending} onClick={() => verify.mutate()}>
            Open my organization
          </button>
        )}
      </section>
    </main>
  );
}
