import { useEffect, useState, type FormEvent } from 'react';
import { useOrganization, useRenameOrganization, useRequireTwoFactor } from '../../data/tenancy';
import { CheckboxField, TextField } from '../../shared/form/fields';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useCan } from '../auth/AuthContext';

const regionNames: Record<string, string> = { asia: 'Asia', us: 'United States' };

/** The organization's name (owners change it) and its data region (fixed at setup). */
export default function OrganizationPage() {
  const organization = useOrganization();
  const rename = useRenameOrganization();
  const requireTwoFactor = useRequireTwoFactor();
  const canRename = useCan('organization.manage');
  const [name, setName] = useState('');

  useEffect(() => {
    if (organization.data) setName(organization.data.name);
  }, [organization.data]);

  function submit(event: FormEvent) {
    event.preventDefault();
    rename.mutate(name);
  }

  return (
    <section className="stack">
      <h1>Organization</h1>
      <ErrorMessage error={organization.error} />
      {organization.data && (
        <form onSubmit={submit} className="stack narrow" aria-label="Organization">
          <TextField label="Name" required maxLength={100} disabled={!canRename} value={name} onChange={setName} />
          <dl className="facts">
            <dt>Data region</dt>
            <dd>{regionNames[organization.data.region] ?? organization.data.region}</dd>
          </dl>
          <p className="muted small">All of the organization's data is kept in this region. It was chosen at setup and cannot change.</p>
          <ErrorMessage error={rename.error} />
          {rename.isSuccess && (
            <p role="status" className="alert alert-ok">
              Saved.
            </p>
          )}
          {canRename && (
            <div className="actions start">
              <button type="submit" className="primary" disabled={rename.isPending || !name.trim()}>
                Save
              </button>
            </div>
          )}
        </form>
      )}
      {organization.data && (
        <div className="stack narrow">
          <h2>Sign-in</h2>
          <CheckboxField
            label="Require two-factor sign-in for everyone"
            checked={organization.data.requireTwoFactor === true}
            disabled={!canRename || requireTwoFactor.isPending}
            onChange={(required) => requireTwoFactor.mutate(required)}
          />
          <p className="muted small">Members without an authenticator app set one up at their next sign-in, before anything else.</p>
          <ErrorMessage error={requireTwoFactor.error} />
        </div>
      )}
    </section>
  );
}
