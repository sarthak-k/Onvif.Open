import { memo } from 'react';
import type { Role, Site, User, UserRequest } from '../../api/types';
import { formatDate } from '../../shared/format';
import { describeSites } from './SiteAccessField';

interface Props {
  user: User;
  isMe: boolean;
  /** The roles the signed-in user may give (only owners give or change Owner). */
  roleOptions: readonly Role[];
  sites: readonly Site[];
  onEditSites(user: User): void;
  onChange(id: string, change: UserRequest): void;
  onResetPassword(user: User): void;
  onResetTwoFactor(user: User): void;
  onDelete(user: User): void;
}

export const UserRow = memo(function UserRow({ user, isMe, roleOptions, sites, onEditSites, onChange, onResetPassword, onResetTwoFactor, onDelete }: Props) {
  // An owner's row can only be changed by an owner.
  const locked = !roleOptions.includes(user.role);

  return (
    <tr>
      <td>
        {user.userName}
        {isMe && <span className="badge">you</span>}
        {user.lockedUntil && <span className="badge warn">locked</span>}
        {user.mustChangePassword && <span className="badge">must change password</span>}
        {user.twoFactor === 'On' && <span className="badge">two-factor</span>}
      </td>
      <td>
        <select aria-label={`Role of ${user.userName}`} value={user.role} disabled={locked} onChange={(e) => onChange(user.id, { role: e.target.value as Role })}>
          {(locked ? [user.role] : roleOptions).map((role) => (
            <option key={role}>{role}</option>
          ))}
        </select>
      </td>
      <td>
        {describeSites(user.sites, sites)}
        {!locked && sites.length > 1 && (
          <button type="button" className="link small-gap" onClick={() => onEditSites(user)} aria-label={`Change sites of ${user.userName}`}>
            Change
          </button>
        )}
      </td>
      <td>
        <input type="checkbox" aria-label={`${user.userName} enabled`} checked={user.enabled} disabled={locked} onChange={(e) => onChange(user.id, { enabled: e.target.checked })} />
      </td>
      <td>{formatDate(user.lastLoginAt)}</td>
      <td className="row-actions">
        <button type="button" disabled={locked} onClick={() => onResetPassword(user)}>
          Reset password
        </button>
        {user.twoFactor && user.twoFactor !== 'Off' && !isMe && (
          <button type="button" disabled={locked} onClick={() => onResetTwoFactor(user)}>
            Reset two-factor
          </button>
        )}
        <button type="button" className="danger" disabled={isMe || locked} onClick={() => onDelete(user)}>
          Delete
        </button>
      </td>
    </tr>
  );
});
