import { useUser } from '../auth/AuthContext';
import { PasswordForm } from '../components/PasswordForm';
import { formatDate } from '../format';

export function AccountPage() {
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
