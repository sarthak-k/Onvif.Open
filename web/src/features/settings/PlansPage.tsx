import { useState, type FormEvent } from 'react';
import type { ManagedPlan, PlanRequest } from '../../api/types';
import { useBilling, usePlans, useRetirePlan, useSavePlan } from '../../data/billing';
import { NumberField, TextField } from '../../shared/form/fields';
import { formatDate } from '../../shared/format';
import { useForm } from '../../shared/form/useForm';
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';

function price(value: number, currency: string): string {
  return new Intl.NumberFormat(undefined, { style: 'currency', currency }).format(value);
}

/**
 * The plans this service sells (the operator's owners): every version, and who is on it. A plan's price or limits
 * changing while organizations are on it makes a new version; they keep the one they bought.
 */
export default function PlansPage() {
  const plans = usePlans();
  const billing = useBilling();
  const retire = useRetirePlan();
  const [editing, setEditing] = useState<ManagedPlan | 'new' | null>(null);
  const [retiring, setRetiring] = useState<ManagedPlan | null>(null);
  const [showRetired, setShowRetired] = useState(false);
  const currency = billing.data?.enabled ? billing.data.currency : 'USD';
  const shown = (plans.data ?? []).filter((plan) => showRetired || plan.active);

  return (
    <section className="stack">
      <div className="heading">
        <h1>Plans</h1>
        <button type="button" className="primary" onClick={() => setEditing('new')}>
          New plan
        </button>
      </div>
      <p className="muted">
        What this service sells. Changing the price or limits of a plan that organizations are on makes a new version for new customers; they keep the one they
        bought. Every change is in the audit log.
      </p>
      <label className="check">
        <input type="checkbox" checked={showRetired} onChange={(e) => setShowRetired(e.target.checked)} />
        Show retired versions
      </label>
      <ErrorMessage error={plans.error ?? retire.error} />

      <div className="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Plan</th>
              <th>Cameras</th>
              <th>Video</th>
              <th>Price a month</th>
              <th>Provider price</th>
              <th>Organizations</th>
              <th>
                <span className="sr-only">Actions</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {shown.map((plan) => (
              <tr key={plan.id} aria-label={`${plan.name} version ${plan.version}`}>
                <td>
                  {plan.name} <span className="muted small">{plan.code} v{plan.version}</span>
                  {!plan.active && <span className="badge">retired {formatDate(plan.retiredAt)}</span>}
                </td>
                <td>{plan.cameras === 0 ? 'Unlimited' : plan.cameras}</td>
                <td>{plan.storageGB === 0 ? 'Unlimited' : `${plan.storageGB} GB`}</td>
                <td>{price(plan.monthlyPrice, currency)}</td>
                <td>
                  <code>{plan.providerPriceId ?? '—'}</code>
                </td>
                <td>{plan.organizations}</td>
                <td className="row-actions">
                  {plan.active && (
                    <>
                      <button type="button" onClick={() => setEditing(plan)}>
                        Change
                      </button>
                      <button type="button" className="danger" onClick={() => setRetiring(plan)}>
                        Retire
                      </button>
                    </>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {editing && <PlanDialog plan={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      {retiring && (
        <ConfirmDialog
          title={`Retire ${retiring.name}?`}
          confirmLabel="Retire"
          danger
          onCancel={() => setRetiring(null)}
          onConfirm={() => {
            retire.mutate(retiring.id);
            setRetiring(null);
          }}
        >
          It is not offered any more. The {retiring.organizations} organization(s) on it keep it.
        </ConfirmDialog>
      )}
    </section>
  );
}

function PlanDialog({ plan, onClose }: { plan: ManagedPlan | null; onClose(): void }) {
  const save = useSavePlan();
  const { values, set } = useForm({
    code: plan?.code ?? '',
    name: plan?.name ?? '',
    cameras: String(plan?.cameras ?? 0),
    storageGB: String(plan?.storageGB ?? 0),
    monthlyPrice: String(plan?.monthlyPrice ?? 0),
    providerPriceId: plan?.providerPriceId ?? '',
    sortOrder: String(plan?.sortOrder ?? 100),
  });
  const terms = plan && (Number(values.cameras) !== plan.cameras || Number(values.storageGB) !== plan.storageGB || Number(values.monthlyPrice) !== plan.monthlyPrice || (values.providerPriceId || null) !== plan.providerPriceId);

  function submit(event?: FormEvent) {
    event?.preventDefault();
    const request: PlanRequest = {
      code: plan ? undefined : values.code.trim(),
      name: values.name.trim(),
      cameras: Number(values.cameras) || 0,
      storageGB: Number(values.storageGB) || 0,
      monthlyPrice: Number(values.monthlyPrice) || 0,
      providerPriceId: values.providerPriceId.trim() || null,
      sortOrder: Number(values.sortOrder) || 0,
    };
    save.mutate({ id: plan?.id, plan: request }, { onSuccess: onClose });
  }

  return (
    <ConfirmDialog title={plan ? `Change ${plan.name}` : 'New plan'} confirmLabel="Save" busy={save.isPending || !values.name.trim()} onCancel={onClose} onConfirm={() => submit()}>
      <form className="stack" onSubmit={submit} aria-label="Plan">
        {!plan && <TextField label="Code" required placeholder="starter" value={values.code} onChange={(v) => set('code', v)} />}
        <TextField label="Name" required maxLength={100} value={values.name} onChange={(v) => set('name', v)} />
        <div className="row">
          <NumberField label="Cameras (0: unlimited)" step="1" value={values.cameras} onChange={(v) => set('cameras', v)} />
          <NumberField label="Video, GB (0: unlimited)" value={values.storageGB} onChange={(v) => set('storageGB', v)} />
        </div>
        <div className="row">
          <NumberField label="Price a month" value={values.monthlyPrice} onChange={(v) => set('monthlyPrice', v)} />
          <TextField label="Provider price" placeholder="price_..." value={values.providerPriceId} onChange={(v) => set('providerPriceId', v)} />
        </div>
        <NumberField label="Order" step="1" value={values.sortOrder} onChange={(v) => set('sortOrder', v)} />
        {terms && plan && plan.organizations > 0 && (
          <p className="alert alert-ok">
            {plan.organizations} organization(s) are on this plan: they keep it as it is, and new customers get version {plan.version + 1}.
          </p>
        )}
        <ErrorMessage error={save.error} />
      </form>
    </ConfirmDialog>
  );
}
