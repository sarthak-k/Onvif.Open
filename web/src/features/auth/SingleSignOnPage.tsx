import { useEffect, useRef } from 'react';
import { Link, Navigate, useLocation } from 'react-router-dom';
import { useMutation } from '@tanstack/react-query';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useAuth } from './AuthContext';

/** Back from the identity provider: the one-time code in the address (#code=) becomes the session. */
export default function SingleSignOnPage() {
  const code = new URLSearchParams(useLocation().hash.replace(/^#/, '')).get('code') ?? '';
  const { user, completeSingleSignOn } = useAuth();
  const exchange = useMutation({ mutationFn: () => completeSingleSignOn(code) });
  const started = useRef(false);

  // Once: the code works once (and development mode runs effects twice).
  useEffect(() => {
    if (!started.current && code) {
      started.current = true;
      exchange.mutate();
    }
  }, [code, exchange]);

  if (user) return <Navigate to="/home" replace />;

  return (
    <main className="centered">
      <section className="card stack narrow" aria-label="Single sign-on">
        <h1>Signing you in…</h1>
        <ErrorMessage error={exchange.error ?? (code ? null : new Error('This page is opened by your identity provider after you sign in there.'))} />
        {(exchange.isError || !code) && <Link to="/login">Back to sign-in</Link>}
      </section>
    </main>
  );
}
