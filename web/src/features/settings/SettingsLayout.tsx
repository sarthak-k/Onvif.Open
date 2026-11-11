import { NavLink, Outlet } from 'react-router-dom';
import { useUser } from '../auth/AuthContext';
import { can, type Permission } from '../auth/permissions';

/** The settings sections; a new one is one entry here plus its route. */
export const settingsSections: readonly { path: string; label: string; permission: Permission }[] = [
  { path: '/settings/sites', label: 'Sites', permission: 'settings.manage' },
  { path: '/settings/policies', label: 'Recording policies', permission: 'settings.manage' },
  { path: '/settings/alerts', label: 'Alert rules', permission: 'settings.manage' },
  { path: '/settings/gateways', label: 'Gateways', permission: 'cameras.view' },
  { path: '/settings/users', label: 'Users', permission: 'users.manage' },
  { path: '/settings/audit', label: 'Audit log', permission: 'audit.view' },
  { path: '/settings/organization', label: 'Organization', permission: 'cameras.view' },
  { path: '/settings/billing', label: 'Billing', permission: 'settings.manage' },
];

export default function SettingsLayout() {
  const user = useUser();

  return (
    <div className="settings">
      <nav aria-label="Settings" className="subnav">
        {settingsSections
          .filter((section) => can(user, section.permission))
          .map((section) => (
            <NavLink key={section.path} to={section.path}>
              {section.label}
            </NavLink>
          ))}
      </nav>
      <div className="settings-body">
        <Outlet />
      </div>
    </div>
  );
}
