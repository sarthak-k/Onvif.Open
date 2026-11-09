import { Suspense } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useAuth, useUser } from '../features/auth/AuthContext';
import { can } from '../features/auth/permissions';
import { ErrorBoundary } from '../shared/ui/ErrorBoundary';
import { navItems } from './routes';

export function Layout() {
  const user = useUser();
  const { signOut } = useAuth();
  const { pathname } = useLocation();
  const nav = navItems.filter((item) => can(user, item.permission));

  return (
    <div className="shell">
      <header className="topbar">
        <span className="brand">Onvif.Open</span>
        <nav aria-label="Main">
          {nav.map((item) => (
            <NavLink key={item.path} to={item.path}>
              {item.label}
            </NavLink>
          ))}
        </nav>
        <span className="who">
          {user.organization.name} · {user.userName} <span className="badge">{user.role}</span>
        </span>
        <button type="button" className="link" onClick={signOut}>
          Sign out
        </button>
      </header>
      <main className="content">
        <ErrorBoundary resetKey={pathname}>
          <Suspense fallback={<p className="loading">Loading…</p>}>
            <Outlet />
          </Suspense>
        </ErrorBoundary>
      </main>
    </div>
  );
}
