import { memo } from 'react';
import { roles, type Role, type User, type UserRequest } from '../../api/types';
import { formatDate } from '../../shared/format';

interface Props {
  user: User;
  isMe: boolean;
  onChange(id: string, change: UserRequest): void;
  onResetPassword(user: User): void;
  onDelete(user: User): void;
}

export const UserRow = memo(function UserRow({ user, isMe, onChange, onResetPassword, onDelete }: Props) {
  return (
    <tr>
      <td>
        {user.userName}
        {isMe && <span className="badge">you</span>}
        {user.lockedUntil && <span className="badge warn">locked</span>}
        {user.mustChangePassword && <span className="badge">must change password</span>}
      </td>
      <td>
        <select aria-label={`Role of ${user.userName}`} value={user.role} onChange={(e) => onChange(user.id, { role: e.target.value as Role })}>
          {roles.map((role) => (
            <option key={role}>{role}</option>
          ))}
        </select>
      </td>
      <td>
        <input type="checkbox" aria-label={`${user.userName} enabled`} checked={user.enabled} onChange={(e) => onChange(user.id, { enabled: e.target.checked })} />
      </td>
      <td>{formatDate(user.lastLoginAt)}</td>
      <td className="row-actions">
        <button type="button" onClick={() => onResetPassword(user)}>
          Reset password
        </button>
        <button type="button" className="danger" disabled={isMe} onClick={() => onDelete(user)}>
          Delete
        </button>
      </td>
    </tr>
  );
});
