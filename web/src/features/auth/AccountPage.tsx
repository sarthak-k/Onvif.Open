import { useUser } from './AuthContext';
import { PasswordForm } from './PasswordForm';
import { formatDate } from '../../shared/format';

export default function AccountPage() {
  const user = useUser();

  return (
    <section className="stack">
      <h1>Account</h1>
      <dl className="facts">
        <dt>User name</dt>
        <dd>{user.userName}</dd>
        <dt>Role</dt>
        <dd>{user.role}</dd>
        <dt>Last sign-in</dt>
        <dd>{formatDate(user.lastLoginAt)}</dd>
      </dl>
      <h2>Change password</h2>
      <PasswordForm />
    </section>
  );
}
