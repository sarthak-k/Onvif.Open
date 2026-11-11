import { useEffect, useState, type FormEvent } from 'react';
import { useMutation } from '@tanstack/react-query';
import QRCode from 'qrcode';
import type { TwoFactorSetup as Setup } from '../../api/types';
import { useApi } from '../../data/ApiContext';
import { TextField } from '../../shared/form/fields';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useAuth } from './AuthContext';
import { twoFactorSetupSteps, type TwoFactorSetupStep } from './twoFactorSetup';

/** A picture of the URI for the phone to scan (SVG, drawn here: the secret never leaves the page). */
function QrCode({ text }: { text: string }) {
  const [svg, setSvg] = useState<string | null>(null);
  useEffect(() => {
    let active = true;
    void QRCode.toString(text, { type: 'svg', margin: 1, errorCorrectionLevel: 'M' }).then((drawn) => active && setSvg(drawn));
    return () => {
      active = false;
    };
  }, [text]);
  return svg ? <img className="qr" width={192} height={192} src={`data:image/svg+xml;utf8,${encodeURIComponent(svg)}`} alt="QR code for your authenticator app" /> : null;
}

/** The recovery codes, shown once: each signs in once without the phone. */
export function RecoveryCodes({ codes, onDone }: { codes: string[]; onDone(): void }) {
  return (
    <div className="stack" aria-label="Recovery codes">
      <p>
        Keep these recovery codes somewhere safe, away from your phone. Each one signs you in once if you lose it. They are <strong>not shown again</strong>.
      </p>
      <ul className="codes">
        {codes.map((code) => (
          <li key={code}>
            <code>{code}</code>
          </li>
        ))}
      </ul>
      <div className="actions start">
        <button type="button" onClick={() => void navigator.clipboard?.writeText(codes.join('\n'))}>
          Copy
        </button>
        <button type="button" className="primary" onClick={onDone}>
          I saved them
        </button>
      </div>
    </div>
  );
}

/** Sets up two-factor sign-in: scan (or type) the secret, enter a code from the app, keep the recovery codes. */
export function TwoFactorSetup({ onDone, onCancel }: { onDone(): void; onCancel?(): void }) {
  const api = useApi();
  const { enableTwoFactor } = useAuth();
  const [step, setStep] = useState<TwoFactorSetupStep>(twoFactorSetupSteps.initial);
  const [setup, setSetup] = useState<Setup | null>(null);
  const [codes, setCodes] = useState<string[]>([]);
  const [code, setCode] = useState('');
  const begin = useMutation({
    mutationFn: api.auth.twoFactor.setup,
    onSuccess: (made) => {
      setSetup(made);
      setCode('');
      setStep((current) => twoFactorSetupSteps.move(current, 'scan'));
    },
  });
  const enable = useMutation({
    mutationFn: () => enableTwoFactor(code),
    onSuccess: (recoveryCodes) => {
      setCodes(recoveryCodes);
      setStep((current) => twoFactorSetupSteps.move(current, 'codes'));
    },
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    enable.mutate();
  }

  function cancel() {
    enable.reset();
    setStep((current) => twoFactorSetupSteps.move(current, 'start'));
    onCancel?.();
  }

  switch (step) {
    case 'start':
      return (
        <div className="stack">
          <p>Sign in with your password and a code from an authenticator app on your phone (Google Authenticator, Microsoft Authenticator, 1Password…).</p>
          <ErrorMessage error={begin.error} />
          <div className="actions start">
            <button type="button" className="primary" disabled={begin.isPending} onClick={() => begin.mutate()}>
              Set up two-factor sign-in
            </button>
          </div>
        </div>
      );
    case 'scan':
      return (
        <form className="stack" onSubmit={submit} aria-label="Set up two-factor sign-in">
          <p>Scan this with your authenticator app, then enter the code it shows.</p>
          {setup && <QrCode text={setup.uri} />}
          <p className="muted small">
            Can't scan? Enter this key in the app: <code className="secret">{setup?.secret.replace(/(.{4})/g, '$1 ').trim()}</code>
          </p>
          <TextField label="Code from the app" inputMode="numeric" autoComplete="one-time-code" required autoFocus maxLength={7} value={code} onChange={setCode} />
          <ErrorMessage error={enable.error} />
          <div className="actions start">
            <button type="submit" className="primary" disabled={enable.isPending || !code.trim()}>
              Turn on
            </button>
            <button type="button" onClick={cancel}>
              Cancel
            </button>
          </div>
        </form>
      );
    case 'codes':
      return <RecoveryCodes codes={codes} onDone={onDone} />;
  }
}
