import { memo } from 'react';
import type { ProbeProfile, ProbeResult } from '../../../api/types';
import { capabilityLabels } from './options';

interface Props {
  result: ProbeResult;
  profileToken: string;
  onSelectProfile(token: string): void;
}

const describeVideo = (p: ProbeProfile) =>
  [p.encoding, p.width && p.height ? `${p.width}×${p.height}` : null, p.frameRate ? `${p.frameRate} fps` : null].filter(Boolean).join(', ');

/** What the connection test found: device, capabilities, and the profiles to choose from. */
export const ProbeReport = memo(function ProbeReport({ result, profileToken, onSelectProfile }: Props) {
  if (!result.ok) {
    return (
      <div role="alert" className="alert alert-error">
        {result.kind === 'unauthorized' ? 'The camera refused the user name or password. ' : 'The camera cannot be reached. '}
        {result.error}
      </div>
    );
  }

  const capabilities = Object.entries(result.capabilities ?? {})
    .filter(([, supported]) => supported)
    .map(([name]) => capabilityLabels[name] ?? name);

  return (
    <div role="status" className="alert alert-ok stack">
      <strong>Connected.</strong>
      {result.device && (
        <span>
          {[result.device.manufacturer, result.device.model].filter(Boolean).join(' ')}
          {result.device.firmwareVersion && `, firmware ${result.device.firmwareVersion}`}
        </span>
      )}
      {result.stream && <span>Stream: {[result.stream.codec, result.stream.audio].filter(Boolean).join(' + ')}</span>}
      {capabilities.length > 0 && <span>Supports: {capabilities.join(', ')}</span>}
      {!!result.profiles?.length && (
        <table className="compact">
          <caption>Media profiles</caption>
          <thead>
            <tr>
              <th>Record</th>
              <th>Profile</th>
              <th>Video</th>
              <th>Features</th>
            </tr>
          </thead>
          <tbody>
            {result.profiles.map((profile, index) => (
              <tr key={profile.token}>
                <td>
                  <input
                    type="radio"
                    name="profile"
                    aria-label={`Record profile ${profile.name ?? profile.token}`}
                    checked={profileToken ? profileToken === profile.token : index === 0}
                    onChange={() => onSelectProfile(profile.token)}
                  />
                </td>
                <td>
                  {profile.name ?? profile.token} <span className="muted small">{profile.token}</span>
                </td>
                <td>{describeVideo(profile)}</td>
                <td>{[profile.ptz && 'PTZ', profile.analytics && 'Analytics'].filter(Boolean).join(', ')}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
});
