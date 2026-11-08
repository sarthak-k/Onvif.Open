import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import type { Role } from '../api/types';
import { hasRole, useAuth } from './AuthContext';

/** Signed-in users only; sends a user who must change the password to that page first. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const location = useLocation();

  if (user === undefined) return <p className="loading">Loading…</p>;
  if (user === null) return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  if (user.mustChangePassword && location.pathname !== '/change-password') return <Navigate to="/change-password" replace />;
  return <>{children}</>;
}

/** Hides a page from roles below the given one (the recorder enforces the same). */
export function RequireRole({ role, children }: { role: Role; children: ReactNode }) {
  const { user } = useAuth();
  return hasRole(user ?? null, role) ? <>{children}</> : <p role="alert">This page needs the {role} role.</p>;
}
