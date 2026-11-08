import { ApiError } from '../api/client';

/** The recorder's messages for a failed call (or nothing). */
export function ErrorMessage({ error }: { error: unknown }) {
  if (!error) return null;
  const messages = error instanceof ApiError ? error.errors : [error instanceof Error ? error.message : String(error)];
  if (messages.length === 0) return null;

  return (
    <div role="alert" className="alert alert-error">
      {messages.length === 1 ? messages[0] : <ul>{messages.map((m) => <li key={m}>{m}</li>)}</ul>}
    </div>
  );
}
