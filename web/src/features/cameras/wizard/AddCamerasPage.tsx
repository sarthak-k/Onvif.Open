import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { useApi } from '../../../data/ApiContext';
import { useCameraConfigs, useDiscovery } from '../../../data/cameras';
import { queryKeys } from '../../../data/queryKeys';
import { usePolicies, useSites } from '../../../data/tenancy';
import { SelectField, TextField } from '../../../shared/form/fields';
import { ErrorMessage } from '../../../shared/ui/ErrorMessage';
import { messageOf } from '../../../api/errors';
import { wizardSteps, type Step } from './steps';
import {
  afterSharedChange,
  changeCandidate,
  credentialsOf,
  describeProfile,
  eachLimited,
  fromAddress,
  fromDevice,
  startTest,
  stateLabels,
  toCameraConfig,
  withProbe,
  type Candidate,
  type SharedCredentials,
} from './candidates';


const steps: { id: Step; label: string }[] = [
  { id: 'find', label: 'Find' },
  { id: 'connect', label: 'Sign in' },
  { id: 'name', label: 'Name and record' },
  { id: 'done', label: 'Done' },
];

/** At most this many cameras are tested at once. */
const parallelTests = 4;

/**
 * Adds several cameras in a few steps: pick what the gateway found, sign in once for all of them, check each picture,
 * name them and choose how they record. Streams are chosen automatically (Advanced to change).
 */
export default function AddCamerasPage() {
  const api = useApi();
  const queries = useQueryClient();
  const configs = useCameraConfigs();
  const sites = useSites();
  const policies = usePolicies();
  const find = useDiscovery();
  const [step, setStep] = useState<Step>('find');
  const [candidates, setCandidates] = useState<Candidate[]>([]);
  const [shared, setShared] = useState<SharedCredentials>({ username: 'admin', password: '' });
  const [testing, setTesting] = useState(false);
  const [adding, setAdding] = useState(false);

  const taken = useMemo(() => configs.data?.map((c) => c.name) ?? [], [configs.data]);
  const update = (key: string, change: Partial<Candidate>) => setCandidates((all) => all.map((c) => (c.key === key ? changeCandidate(c, change) : c)));
  const go = (to: Step) => setStep((from) => wizardSteps.move(from, to));
  const changeShared = (next: SharedCredentials) => {
    setShared(next);
    setCandidates(afterSharedChange);
  };
  const selected = candidates.filter((c) => c.selected);
  const ready = selected.filter((c) => c.state === 'ok');

  // Look for cameras as soon as the wizard opens.
  const { mutate: search } = find;
  useEffect(() => search(), [search]);

  // Found devices join the list (not those already added, nor ones already listed).
  useEffect(() => {
    if (!find.data) {
      return;
    }
    setCandidates((all) => {
      const known = new Set(all.map((c) => c.key));
      const found = find.data
        .filter((d) => !d.configuredCameraId)
        .map((d) => fromDevice(d, [...taken, ...all.map((c) => c.name)]))
        .filter((c): c is Candidate => c !== null && !known.has(c.key));
      return [...all, ...found];
    });
  }, [find.data, taken]);

  // Each camera starts at the first site, with the default policy.
  const firstSite = sites.data?.[0]?.id ?? '';
  useEffect(() => {
    if (firstSite) {
      setCandidates((all) => all.map((c) => (c.siteId ? c : { ...c, siteId: firstSite })));
    }
  }, [firstSite, candidates.length]);

  async function test(only?: Candidate[]) {
    setTesting(true);
    const targets = only ?? selected.filter((c) => c.state !== 'ok');
    const keys = new Set(targets.map((c) => c.key));
    setCandidates((all) => all.map((c) => (keys.has(c.key) ? startTest(c) : c)));
    await eachLimited(targets, parallelTests, async (candidate) => {
      const { username, password } = credentialsOf(candidate, shared);
      try {
        const result = await api.cameras.test({ onvifUrl: candidate.onvifUrl, username, password }, { snapshot: true });
        setCandidates((all) => all.map((c) => (c.key === candidate.key ? withProbe(c, result) : c)));
      } catch (error) {
        update(candidate.key, { state: 'unreachable', error: messageOf(error) });
      }
    });
    setTesting(false);
  }

  async function addAll() {
    setAdding(true);
    for (const candidate of ready) {
      try {
        const camera = await api.cameras.create(toCameraConfig(candidate, shared));
        update(candidate.key, { addedId: camera.id, addError: undefined });
      } catch (error) {
        update(candidate.key, { addError: messageOf(error) });
      }
    }
    await queries.invalidateQueries({ queryKey: queryKeys.cameras.all });
    setAdding(false);
    go('done');
  }

  return (
    <section className="stack">
      <div className="heading">
        <h1>Add cameras</h1>
        <Link to="/cameras/new">Add one manually</Link>
      </div>
      <ol className="steps" aria-label="Steps">
        {steps.map((s) => (
          <li key={s.id} aria-current={s.id === step ? 'step' : undefined}>
            {s.label}
          </li>
        ))}
      </ol>

      {step === 'find' && (
        <FindStep
          candidates={candidates}
          searching={find.isPending}
          error={find.error}
          onSearch={() => find.mutate()}
          onToggle={(c) => update(c.key, { selected: !c.selected })}
          onAddAddress={(address) => {
            const candidate = fromAddress(address, [...taken, ...candidates.map((c) => c.name)]);
            if (candidate && !candidates.some((c) => c.key === candidate.key)) {
              setCandidates((all) => [...all, { ...candidate, siteId: firstSite }]);
            }
            return candidate !== null;
          }}
          onNext={() => go('connect')}
        />
      )}

      {step === 'connect' && (
        <ConnectStep
          candidates={selected}
          shared={shared}
          testing={testing}
          onShared={changeShared}
          onChange={update}
          onTest={test}
          onBack={() => go('find')}
          onNext={() => go('name')}
        />
      )}

      {step === 'name' && (
        <NameStep
          candidates={ready}
          sites={sites.data ?? []}
          policies={policies.data ?? []}
          adding={adding}
          onChange={update}
          onBack={() => go('connect')}
          onAdd={addAll}
        />
      )}

      {step === 'done' && <DoneStep candidates={ready} />}
    </section>
  );
}

function CameraPicture({ candidate }: { candidate: Candidate }) {
  return candidate.snapshot ? (
    <img className="snapshot" src={candidate.snapshot} alt={`Picture from ${candidate.label}`} />
  ) : (
    <div className="snapshot snapshot-empty" aria-hidden="true">
      No picture
    </div>
  );
}

interface FindProps {
  candidates: Candidate[];
  searching: boolean;
  error: unknown;
  onSearch(): void;
  onToggle(candidate: Candidate): void;
  onAddAddress(address: string): boolean;
  onNext(): void;
}

function FindStep({ candidates, searching, error, onSearch, onToggle, onAddAddress, onNext }: FindProps) {
  const [address, setAddress] = useState('');
  const [invalid, setInvalid] = useState(false);
  const count = candidates.filter((c) => c.selected).length;

  function add(event: FormEvent) {
    event.preventDefault();
    const ok = onAddAddress(address);
    setInvalid(!ok);
    if (ok) setAddress('');
  }

  return (
    <div className="stack">
      <div className="card stack">
        <div className="heading">
          <h2>Cameras on the network</h2>
          <button type="button" onClick={onSearch} disabled={searching}>
            {searching ? 'Searching…' : 'Search again'}
          </button>
        </div>
        <ErrorMessage error={error} />
        {!searching && candidates.length === 0 && <p className="muted">No new cameras answered. Add one by its address below.</p>}
        <ul className="devices">
          {candidates.map((candidate) => (
            <li key={candidate.key}>
              <label className="check">
                <input type="checkbox" checked={candidate.selected} onChange={() => onToggle(candidate)} />
                <span>
                  <strong>{candidate.label}</strong> {candidate.hardware && <span className="muted">{candidate.hardware}</span>}
                  <span className="muted small"> {candidate.onvifUrl}</span>
                </span>
              </label>
            </li>
          ))}
        </ul>
      </div>
      <form className="row" onSubmit={add} aria-label="Add by address">
        <TextField label="Camera address" placeholder="10.0.5.20" value={address} onChange={setAddress} />
        <button type="submit" disabled={!address.trim()}>
          Add to the list
        </button>
      </form>
      {invalid && <p className="alert alert-error">That is not an address. Use an IP address or a host name, e.g. 10.0.5.20.</p>}
      <div className="actions">
        <button type="button" className="primary" disabled={count === 0} onClick={onNext}>
          {count === 0 ? 'Choose cameras' : `Continue with ${count} camera${count === 1 ? '' : 's'}`}
        </button>
      </div>
    </div>
  );
}

interface ConnectProps {
  candidates: Candidate[];
  shared: SharedCredentials;
  testing: boolean;
  onShared(shared: SharedCredentials): void;
  onChange(key: string, change: Partial<Candidate>): void;
  onTest(only?: Candidate[]): void;
  onBack(): void;
  onNext(): void;
}

function ConnectStep({ candidates, shared, testing, onShared, onChange, onTest, onBack, onNext }: ConnectProps) {
  const ready = candidates.filter((c) => c.state === 'ok').length;
  const tested = candidates.some((c) => c.state !== 'untested');

  return (
    <div className="stack">
      <form
        className="card stack"
        aria-label="Camera sign-in"
        onSubmit={(event) => {
          event.preventDefault();
          onTest();
        }}
      >
        <h2>Sign in to the cameras</h2>
        <p className="muted small">Most installations use one account on every camera. A camera that refuses it can have its own below.</p>
        <div className="row">
          <TextField label="User name" autoComplete="off" disabled={testing} value={shared.username} onChange={(username) => onShared({ ...shared, username })} />
          <TextField label="Password" type="password" autoComplete="new-password" disabled={testing} value={shared.password} onChange={(password) => onShared({ ...shared, password })} />
          <button type="submit" disabled={testing}>
            {testing ? 'Testing…' : tested ? 'Test again' : 'Test cameras'}
          </button>
        </div>
      </form>

      <ul className="candidates">
        {candidates.map((candidate) => (
          <li key={candidate.key} className="card" aria-label={candidate.label}>
            <CameraPicture candidate={candidate} />
            <div className="stack">
              <strong>{candidate.label}</strong>
              <span className={`state state-${candidate.state}`}>{stateLabels[candidate.state]}</span>
              {candidate.error && candidate.state !== 'unauthorized' && <span className="muted small">{candidate.error}</span>}
              {candidate.state === 'unauthorized' && !candidate.ownCredentials && (
                <button type="button" className="link" onClick={() => onChange(candidate.key, { ownCredentials: true, username: shared.username })}>
                  Use its own password
                </button>
              )}
              {candidate.ownCredentials && candidate.state !== 'ok' && (
                <div className="row">
                  <TextField label={`User name for ${candidate.label}`} autoComplete="off" disabled={testing} value={candidate.username} onChange={(username) => onChange(candidate.key, { username })} />
                  <TextField label={`Password for ${candidate.label}`} type="password" autoComplete="new-password" disabled={testing} value={candidate.password} onChange={(password) => onChange(candidate.key, { password })} />
                  <button type="button" disabled={testing} onClick={() => onTest([candidate])}>
                    Try again
                  </button>
                </div>
              )}
            </div>
          </li>
        ))}
      </ul>

      <div className="actions">
        <button type="button" onClick={onBack}>
          Back
        </button>
        <button type="button" className="primary" disabled={ready === 0 || testing} onClick={onNext}>
          {ready === 0 ? 'Test the cameras first' : `Continue with ${ready} camera${ready === 1 ? '' : 's'}`}
        </button>
      </div>
      {ready > 0 && ready < candidates.length && <p className="muted small">Cameras that are not ready are left out; you can add them later.</p>}
    </div>
  );
}

interface NameProps {
  candidates: Candidate[];
  sites: { id: string; name: string }[];
  policies: { id: string; name: string; isDefault: boolean }[];
  adding: boolean;
  onChange(key: string, change: Partial<Candidate>): void;
  onBack(): void;
  onAdd(): void;
}

function NameStep({ candidates, sites, policies, adding, onChange, onBack, onAdd }: NameProps) {
  const defaultPolicy = policies.find((p) => p.isDefault);
  const policyOptions = [
    { value: '', label: defaultPolicy ? `${defaultPolicy.name} (default)` : 'Default' },
    ...policies.filter((p) => !p.isDefault).map((p) => ({ value: p.id, label: p.name })),
  ];
  const siteOptions = sites.map((s) => ({ value: s.id, label: s.name }));
  const unnamed = candidates.some((c) => !c.name.trim());

  return (
    <div className="stack">
      <ul className="candidates">
        {candidates.map((candidate) => (
          <li key={candidate.key} className="card" aria-label={candidate.label}>
            <CameraPicture candidate={candidate} />
            <div className="stack">
              <TextField label={`Name of ${candidate.label}`} required maxLength={100} value={candidate.name} onChange={(name) => onChange(candidate.key, { name })} />
              <div className="row">
                {siteOptions.length > 1 && (
                  <SelectField label={`Site of ${candidate.label}`} value={candidate.siteId} options={siteOptions} onChange={(siteId) => onChange(candidate.key, { siteId })} />
                )}
                <SelectField label={`Recording of ${candidate.label}`} value={candidate.policyId} options={policyOptions} onChange={(policyId) => onChange(candidate.key, { policyId })} />
              </div>
              {candidate.profiles.length > 1 && (
                <details>
                  <summary>Advanced</summary>
                  <SelectField
                    label={`Stream to record for ${candidate.label}`}
                    value={candidate.profileToken}
                    options={candidate.profiles.map((p) => ({ value: p.token, label: describeProfile(p) }))}
                    onChange={(profileToken) => onChange(candidate.key, { profileToken })}
                  />
                </details>
              )}
            </div>
          </li>
        ))}
      </ul>
      <div className="actions">
        <button type="button" onClick={onBack}>
          Back
        </button>
        <button type="button" className="primary" disabled={adding || unnamed} onClick={onAdd}>
          {adding ? 'Adding…' : `Add ${candidates.length} camera${candidates.length === 1 ? '' : 's'}`}
        </button>
      </div>
    </div>
  );
}

function DoneStep({ candidates }: { candidates: Candidate[] }) {
  const added = candidates.filter((c) => c.addedId);
  const failed = candidates.filter((c) => c.addError);

  return (
    <div className="stack">
      {added.length > 0 && (
        <p className="alert alert-ok">
          {added.length} camera{added.length === 1 ? ' is' : 's are'} recording: {added.map((c) => c.name).join(', ')}.
        </p>
      )}
      {failed.map((c) => (
        <p key={c.key} className="alert alert-error">
          {c.name}: {c.addError}
        </p>
      ))}
      <div className="actions">
        <Link to="/cameras">Back to cameras</Link>
        <Link className="button primary" to="/live">
          Watch live
        </Link>
      </div>
    </div>
  );
}
