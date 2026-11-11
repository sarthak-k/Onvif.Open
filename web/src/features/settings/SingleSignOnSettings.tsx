import { useEffect, type FormEvent } from 'react';
import type { Role, SsoSettings } from '../../api/types';
import { useApi } from '../../data/ApiContext';
import { useSaveSingleSignOn, useSingleSignOn } from '../../data/tenancy';
import { CheckboxField, SelectField, TextAreaField, TextField } from '../../shared/form/fields';
import { useForm } from '../../shared/form/useForm';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';

const roles: { value: Role; label: string }[] = [
  { value: 'Viewer', label: 'Viewer' },
  { value: 'Operator', label: 'Operator' },
  { value: 'Admin', label: 'Admin' },
];

function toForm(settings?: SsoSettings) {
  return {
    enabled: settings?.enabled ?? false,
    issuer: settings?.issuer ?? '',
    clientId: settings?.clientId ?? '',
    clientSecret: '',
    domains: (settings?.domains ?? []).join('\n'),
    autoCreate: settings?.autoCreate ?? false,
    defaultRole: (settings?.defaultRole ?? 'Viewer') as Role,
    required: settings?.required ?? false,
  };
}

/** The organization's identity provider (OpenID Connect): owners set it up, administrators see it. */
export function SingleSignOnSettings({ canEdit }: { canEdit: boolean }) {
  const api = useApi();
  const sso = useSingleSignOn();
  const save = useSaveSingleSignOn();
  const { values, set, setValues } = useForm(toForm());

  useEffect(() => {
    if (sso.data) setValues(toForm(sso.data));
  }, [sso.data, setValues]);

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate({
      enabled: values.enabled,
      issuer: values.issuer.trim() || null,
      clientId: values.clientId.trim() || null,
      clientSecret: values.clientSecret ? values.clientSecret : null,
      domains: values.domains.split(/[\s,]+/).filter(Boolean),
      autoCreate: values.autoCreate,
      defaultRole: values.defaultRole,
      required: values.required,
    });
  }

  return (
    <form className="stack narrow" onSubmit={submit} aria-label="Single sign-on">
      <h2>Single sign-on</h2>
      <p className="muted small">
        People sign in with your organization's account at its identity provider (OpenID Connect: Microsoft Entra ID, Google Workspace, Okta…). Register
        Onvif.Open there with the redirect address <code>{api.gateways.controlPlaneUrl()}/api/auth/sso/callback</code>.
      </p>
      <ErrorMessage error={sso.error} />
      <CheckboxField label="Sign in with the identity provider" checked={values.enabled} disabled={!canEdit} onChange={(v) => set('enabled', v)} />
      <TextField label="Issuer" type="url" placeholder="https://login.microsoftonline.com/{tenant}/v2.0" disabled={!canEdit} value={values.issuer} onChange={(v) => set('issuer', v)} />
      <div className="row">
        <TextField label="Client ID" disabled={!canEdit} value={values.clientId} onChange={(v) => set('clientId', v)} />
        <TextField
          label="Client secret"
          type="password"
          autoComplete="off"
          disabled={!canEdit}
          placeholder={sso.data?.hasClientSecret ? 'Stored. Leave empty to keep it.' : ''}
          value={values.clientSecret}
          onChange={(v) => set('clientSecret', v)}
        />
      </div>
      <TextAreaField label="Email domains (one per line)" rows={2} placeholder="riverside-shops.com" value={values.domains} onChange={(v) => set('domains', v)} />
      <CheckboxField label="Make accounts at the first sign-in, for these domains" checked={values.autoCreate} disabled={!canEdit} onChange={(v) => set('autoCreate', v)} />
      {values.autoCreate && <SelectField label="New accounts are" value={values.defaultRole} options={roles} disabled={!canEdit} onChange={(v) => set('defaultRole', v)} />}
      <CheckboxField label="Require it: no passwords except owners'" checked={values.required} disabled={!canEdit || !values.enabled} onChange={(v) => set('required', v)} />
      <p className="muted small">Owners keep their password, to get back in if the identity provider fails. Its own second factor replaces ours.</p>
      <ErrorMessage error={save.error} />
      {save.isSuccess && (
        <p role="status" className="alert alert-ok">
          Saved.
        </p>
      )}
      {canEdit && (
        <div className="actions start">
          <button type="submit" className="primary" disabled={save.isPending}>
            Save single sign-on
          </button>
        </div>
      )}
    </form>
  );
}
