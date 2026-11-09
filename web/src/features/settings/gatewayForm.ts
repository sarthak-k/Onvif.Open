import type { Gateway, GatewayRequest } from '../../api/types';
import { formatBytes } from '../../shared/format';

/** Where backups can go: built in, or the storage plugins that ship with the recorder. */
export const backupDestinations = [
  { value: '', label: 'No backup: keep recordings on the gateway' },
  { value: 'FileSystem', label: 'A folder or NAS share' },
  { value: 'Onvif.Open.Storage.S3', label: 'Amazon S3 (or S3-compatible)' },
  { value: 'Onvif.Open.Storage.Azure', label: 'Azure Blob Storage' },
  { value: 'Onvif.Open.Storage.Gcs', label: 'Google Cloud Storage' },
  { value: 'other', label: 'Another storage plugin' },
] as const;

/** The settings each destination needs, as hints. */
export const backupSettingHints: Record<string, string> = {
  FileSystem: 'Path=/mnt/nas/recordings',
  'Onvif.Open.Storage.S3': 'Bucket=my-recordings\nRegion=eu-west-1\nAccessKey=...\nSecretKey=...',
  'Onvif.Open.Storage.Azure': 'ConnectionString=...\nContainer=recordings',
  'Onvif.Open.Storage.Gcs': 'Bucket=my-recordings\nCredentialsFile=/etc/onvif/gcs.json',
};

export interface GatewayFormValues {
  name: string;
  minFreeSpaceGB: string;
  maxSizePerCameraGB: string;
  maxTotalSizeGB: string;
  destination: string;
  otherProvider: string;
  prefix: string;
  keepLocalCopyHours: string;
  /** "Key=Value" per line; empty: keep the stored settings. */
  backupSettings: string;
  multicast: boolean;
  /** One address per line. */
  addresses: string;
}

const known = new Set<string>(backupDestinations.map((d) => d.value).filter((v) => v !== 'other'));

export function toFormValues(gateway: Gateway): GatewayFormValues {
  const provider = gateway.backup?.provider ?? '';
  const gb = (value: number | null | undefined) => (value == null ? '' : String(value));
  return {
    name: gateway.name,
    minFreeSpaceGB: gb(gateway.storage?.minFreeSpaceGB),
    maxSizePerCameraGB: gb(gateway.storage?.maxSizePerCameraGB),
    maxTotalSizeGB: gb(gateway.storage?.maxTotalSizeGB),
    destination: known.has(provider) ? provider : 'other',
    otherProvider: known.has(provider) ? '' : provider,
    prefix: gateway.backup?.prefix ?? '',
    keepLocalCopyHours: String(gateway.backup?.keepLocalCopyHours ?? 0),
    backupSettings: '',
    multicast: gateway.discovery?.multicast ?? true,
    addresses: (gateway.discovery?.addresses ?? []).join('\n'),
  };
}

/** "Key=Value" lines to settings; blank lines are skipped, the first "=" splits. */
export function parseSettings(text: string): Record<string, string> {
  const settings: Record<string, string> = {};
  for (const line of text.split('\n')) {
    const at = line.indexOf('=');
    if (at > 0) {
      settings[line.slice(0, at).trim()] = line.slice(at + 1).trim();
    }
  }
  return settings;
}

export function toRequest(values: GatewayFormValues, gateway: Gateway): GatewayRequest {
  const gb = (value: string) => (value.trim() === '' ? null : Number(value));
  const provider = values.destination === 'other' ? values.otherProvider.trim() : values.destination;
  const sameProvider = provider === (gateway.backup?.provider ?? '');
  return {
    name: values.name.trim(),
    storage: { minFreeSpaceGB: gb(values.minFreeSpaceGB), maxSizePerCameraGB: gb(values.maxSizePerCameraGB), maxTotalSizeGB: gb(values.maxTotalSizeGB) },
    backup: {
      provider,
      prefix: values.prefix.trim() || null,
      keepLocalCopyHours: Number(values.keepLocalCopyHours) || 0,
      // Settings are write-only: none typed keeps the stored ones (for the same destination).
      settings: values.backupSettings.trim() === '' && sameProvider ? null : parseSettings(values.backupSettings),
    },
    discovery: {
      multicast: values.multicast,
      addresses: values.addresses
        .split('\n')
        .map((a) => a.trim())
        .filter(Boolean),
    },
  };
}

/** One line about the gateway's cameras: "3 of 4 cameras recording, 1 reconnecting". */
export function describeCameras(gateway: Gateway): string {
  const health = gateway.health;
  if (!health) {
    return 'No report yet';
  }
  if (health.cameras === 0) {
    return 'No cameras yet';
  }
  const failing = health.failing > 0 ? `, ${health.failing} reconnecting` : '';
  return `${health.recording} of ${health.cameras} camera${health.cameras === 1 ? '' : 's'} recording${failing}`;
}

export function describeStorage(gateway: Gateway): string {
  const health = gateway.health;
  if (!health) {
    return '—';
  }
  const free = health.freeBytes == null ? '' : `, ${formatBytes(health.freeBytes)} free`;
  return `${formatBytes(health.recordedBytes)} recorded${free}`;
}

export function describeBackup(gateway: Gateway): string {
  const health = gateway.health;
  const provider = health?.backupProvider ?? gateway.backup?.provider;
  if (!provider) {
    return 'None';
  }
  const label = backupDestinations.find((d) => d.value === provider)?.label ?? provider;
  if (health?.backupError) {
    return `${label}: ${health.backupError}`;
  }
  return health?.pendingUploads ? `${label}, ${health.pendingUploads} file${health.pendingUploads === 1 ? '' : 's'} waiting` : label;
}
