import { Navigate } from 'react-router-dom';
import { useUser } from '../features/auth/AuthContext';
import { can } from '../features/auth/permissions';
import { settingsSections } from '../features/settings/SettingsLayout';

/** /settings opens the first section the user may use. */
export function SettingsHome() {
  const user = useUser();
  const first = settingsSections.find((section) => can(user, section.permission));
  return <Navigate to={first?.path ?? '/settings/organization'} replace />;
}
