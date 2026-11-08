import { NavLink, Outlet } from 'react-router-dom';
import { hasRole, useAuth, useUser } from '../auth/AuthContext';

export function Layout() {
  const user = useUser();
  const { signOut } = useAuth();

  return (
    <div className="shell">
      <header className="topbar">
        <span className="brand">Onvif.Open</span>
        <nav aria-label="Main">
          <NavLink to="/cameras">Cameras</NavLink>
          {hasRole(user, 'Admin') && <NavLink to="/users">Users</NavLink>}
          <NavLink to="/account">Account</NavLink>
        </nav>
        <span className="who">
          {user.userName} <span className="badge">{user.role}</span>
        </span>
        <button type="button" className="link" onClick={signOut}>
          Sign out
        </button>
      </header>
      <main className="content">
        <Outlet />
      </main>
    </div>
  );
}
