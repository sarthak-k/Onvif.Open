import { useSearchParams } from 'react-router-dom';
import type { BillingStatus, Plan, SubscriptionState } from '../../api/types';
import { useBilling, useBillingPortal, useCheckout } from '../../data/billing';
import { formatBytes, formatDate } from '../../shared/format';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useCan } from '../auth/AuthContext';

const stateLabels: Record<SubscriptionState, string> = { Trialing: 'Trial', Active: 'Active', PastDue: 'Payment due', Canceled: 'Ended' };
const stateClass: Record<SubscriptionState, string> = { Trialing: 'state-recording', Active: 'state-recording', PastDue: 'state-reconnecting', Canceled: 'state-stopped' };

const GB = 1024 ** 3;

function price(plan: Plan, currency: string): string {
  return plan.monthlyPrice === 0 ? 'Free' : `${new Intl.NumberFormat(undefined, { style: 'currency', currency }).format(plan.monthlyPrice)} a month`;
}

function limit(value: number, unit: string): string {
  return value === 0 ? `Unlimited ${unit}` : `${value} ${unit}`;
}

/** How much of a limit is used, as a bar (no bar without a limit). */
function Usage({ label, used, of, text }: { label: string; used: number; of: number; text: string }) {
  const share = of > 0 ? Math.min(used / of, 1) : 0;
  return (
    <div className="stack tight" aria-label={label}>
      <div className="heading">
        <span>{label}</span>
        <span className={of > 0 && used > of ? 'alert-error' : 'muted'}>{text}</span>
      </div>
      {of > 0 && <progress max={1} value={share} aria-label={`${label} used`} />}
    </div>
  );
}

/** The organization's plan: where it stands, what it uses, the plans to choose from (owners pay, at the provider). */
export default function BillingPage() {
  const billing = useBilling();
  const checkout = useCheckout();
  const portal = useBillingPortal();
  const isOwner = useCan('organization.manage');
  const back = useSearchParams()[0].get('checkout') === 'done';
  const status = billing.data;

  return (
    <section className="stack">
      <h1>Billing</h1>
      <ErrorMessage error={billing.error ?? checkout.error ?? portal.error} />
      {status && !status.enabled && <p className="muted">Billing is not used on this server.</p>}
      {status?.enabled && <Billing status={status} isOwner={isOwner} back={back} checkout={checkout} portal={portal} />}
    </section>
  );
}

function Billing({
  status,
  isOwner,
  back,
  checkout,
  portal,
}: {
  status: Extract<BillingStatus, { enabled: true }>;
  isOwner: boolean;
  back: boolean;
  checkout: ReturnType<typeof useCheckout>;
  portal: ReturnType<typeof useBillingPortal>;
}) {
  const plan = status.plan;
  const busy = checkout.isPending || portal.isPending;

  return (
    <>
      {back && (
        <p role="status" className="alert alert-ok">
          Thank you: your plan changes as soon as the payment provider confirms it.
        </p>
      )}
      {status.state === 'PastDue' && <p className="alert alert-error">A payment failed: update your card in Manage billing to keep your plan.</p>}
      {status.state === 'Canceled' && <p className="alert alert-error">Your subscription has ended: choose a plan to add cameras. Cameras you have keep recording.</p>}

      <article className="card stack" aria-label="Your plan">
        <div className="heading">
          <h2>{plan?.name ?? 'No plan'}</h2>
          <span className={`state ${stateClass[status.state]}`}>{stateLabels[status.state]}</span>
        </div>
        {plan && !plan.active && <p className="muted small">This plan is no longer offered: you keep it, as it is, for as long as you stay on it.</p>}
        <p className="muted small">
          {status.state === 'Trialing' && status.trialEndsAt && `Trial until ${formatDate(status.trialEndsAt)}. `}
          {status.state !== 'Trialing' && status.currentPeriodEnd && `Paid until ${formatDate(status.currentPeriodEnd)}. `}
          {plan && price(plan, status.currency)}
        </p>
        <Usage label="Cameras" used={status.usage.cameras} of={plan?.cameras ?? 0} text={`${status.usage.cameras} of ${plan?.cameras ? plan.cameras : 'unlimited'}`} />
        <Usage
          label="Recorded video"
          used={status.usage.storageBytes}
          of={(plan?.storageGB ?? 0) * GB}
          text={`${formatBytes(status.usage.storageBytes)} of ${plan?.storageGB ? formatBytes(plan.storageGB * GB) : 'unlimited'}`}
        />
        {(status.overCameras || status.overStorage) && <p className="alert alert-error">You use more than your plan includes: choose a larger plan.</p>}
        {isOwner && status.canManage && (
          <div className="actions start">
            <button type="button" disabled={busy} onClick={() => portal.mutate()}>
              Manage billing
            </button>
          </div>
        )}
      </article>

      <h2>Plans</h2>
      <div className="plans">
        {status.plans.map((option) => (
          <article key={option.id} className={`card stack${option.id === plan?.id ? ' current' : ''}`} aria-label={option.name}>
            <h3>{option.name}</h3>
            <p className="price">{price(option, status.currency)}</p>
            <ul className="small">
              <li>{limit(option.cameras, 'cameras')}</li>
              <li>{option.storageGB === 0 ? 'Unlimited video' : `${formatBytes(option.storageGB * GB)} of video`}</li>
            </ul>
            {option.id === plan?.id ? (
              <span className="muted small">Your plan</span>
            ) : (
              isOwner &&
              option.purchasable &&
              (status.canPay ? (
                <button
                  type="button"
                  className="primary"
                  disabled={busy}
                  onClick={() => (status.canManage && status.state !== 'Canceled' ? portal.mutate() : checkout.mutate(option.id))}
                >
                  Choose {option.name}
                </button>
              ) : (
                status.salesEmail && <a href={`mailto:${status.salesEmail}?subject=${encodeURIComponent(`Onvif.Open ${option.name} plan`)}`}>Contact us</a>
              ))
            )}
          </article>
        ))}
      </div>
      {!isOwner && <p className="muted small">The organization's owners choose its plan.</p>}
    </>
  );
}
