import { Link } from 'react-router-dom';
import type { Me } from '../../api/types';
import { can } from '../auth/permissions';

const DAY = 24 * 60 * 60 * 1000;

/** What an administrator should know about the subscription, on every page: a failed payment, its end, a trial ending soon. */
export function SubscriptionBanner({ user, now = Date.now() }: { user: Me; now?: number }) {
  const subscription = user.organization.subscription;
  if (!subscription || !can(user, 'settings.manage')) return null;

  const daysLeft = subscription.trialEndsAt ? Math.ceil((Date.parse(subscription.trialEndsAt) - now) / DAY) : null;
  const message =
    subscription.state === 'PastDue'
      ? 'A payment failed.'
      : subscription.state === 'Canceled'
        ? 'Your subscription has ended: no new cameras can be added.'
        : subscription.state === 'Trialing' && daysLeft !== null && daysLeft <= 3
          ? `Your trial ends in ${Math.max(daysLeft, 0)} day${daysLeft === 1 ? '' : 's'}.`
          : null;

  return message ? (
    <p role="status" className="alert alert-error banner">
      {message} <Link to="/settings/billing">Billing</Link>
    </p>
  ) : null;
}
