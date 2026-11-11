import { useEffect, type FormEvent } from 'react';
import type { Role, SsoSettings } from '../../api/types';
import { formatDate } from '../../shared/format';
import { useApi } from '../../data/ApiContext';
import { useSaveSingleSignOn, useSingleSignOn } from '../../data/tenancy';
import { CheckboxField, RadioGroup, SelectField, TextAreaField, TextField } from '../../shared/form/fields';
import { useForm } from '../../shared/form/useForm';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';

const protocols = [
  { value: 'Oidc' as const, label: 'OpenID Connect' },
  { value: 'Saml' as const, label: 'SAML 2.0' },
];

const roles: { value: Role; label: string }[] = [
  { value: 'Viewer', label: 'Viewer' },
  { value: 'Operator', label: 'Operator' },
  { value: 'Admin', label: 'Admin' },
];

function toForm(settings?: SsoSettings) {
  return {
    enabled: settings?.enabled ?? false,
    protocol: settings?.protocol ?? ('Oidc' as SsoSettings['protocol']),
    samlEntityId: settings?.samlEntityId ?? '',
    samlSsoUrl: settings?.samlSsoUrl ?? '',
    samlCertificate: settings?.samlCertificate ?? '',
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
  const recorder = api.gateways.controlPlaneUrl();
  const save = useSaveSingleSignOn();
  const { values, set, setValues } = useForm(toForm());

  useEffect(() => {
    if (sso.data) setValues(toForm(sso.data));
  }, [sso.data, setValues]);

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate({
      enabled: values.enabled,
      protocol: values.protocol,
      samlEntityId: values.samlEntityId.trim() || null,
      samlSsoUrl: values.samlSsoUrl.trim() || null,
      samlCertificate: values.samlCertificate.trim() || null,
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
      <p className="muted small">People sign in with your organization's account at its identity provider (Microsoft Entra ID, Google Workspace, Okta, AD FS…).</p>
      <ErrorMessage error={sso.error} />
      <CheckboxField label="Sign in with the identity provider" checked={values.enabled} disabled={!canEdit} onChange={(v) => set('enabled', v)} />
      <RadioGroup legend="Protocol" value={values.protocol} options={protocols} onChange={(v) => canEdit && set('protocol', v)} />
      {values.protocol === 'Oidc' ? (
        <>
          <p className="muted small">
            Register Onvif.Open at the provider with the redirect address <code>{recorder}/api/auth/sso/callback</code>.
          </p>
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
        </>
      ) : (
        <>
          <p className="muted small">
            Add Onvif.Open at the provider from its metadata, <code>{recorder}/api/auth/sso/saml/metadata</code> (entity ID: the same address; responses
            go to <code>{recorder}/api/auth/sso/saml/acs</code>). Sign the assertions with SHA-256 and leave them unencrypted; send the email as the NameID or
            an email attribute. Needs HTTPS.
          </p>
          <TextField label="Provider entity ID" placeholder="https://sts.windows.net/{tenant}/" disabled={!canEdit} value={values.samlEntityId} onChange={(v) => set('samlEntityId', v)} />
          <TextField label="Provider sign-in address" type="url" placeholder="https://idp.example.com/saml/sso" disabled={!canEdit} value={values.samlSsoUrl} onChange={(v) => set('samlSsoUrl', v)} />
          <TextAreaField label="Provider signing certificate" rows={4} placeholder="-----BEGIN CERTIFICATE-----" value={values.samlCertificate} onChange={(v) => set('samlCertificate', v)} />
          {sso.data?.samlCertificateSubject && (
            <p className="muted small">
              {sso.data.samlCertificateSubject}, valid until {formatDate(sso.data.samlCertificateExpires ?? null)}
            </p>
          )}
        </>
      )}
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
