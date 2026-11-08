import { useEffect, useId, useRef, type ReactNode } from 'react';

interface Props {
  title: string;
  children: ReactNode;
  confirmLabel: string;
  danger?: boolean;
  busy?: boolean;
  onConfirm(): void;
  onCancel(): void;
}

/** A modal question; Escape or the backdrop cancels. */
export function ConfirmDialog({ title, children, confirmLabel, danger, busy, onConfirm, onCancel }: Props) {
  const titleId = useId();
  const cancel = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    cancel.current?.focus();
    const onKey = (event: KeyboardEvent) => event.key === 'Escape' && onCancel();
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onCancel]);

  return (
    <div className="backdrop" onClick={onCancel}>
      <div role="dialog" aria-modal="true" aria-labelledby={titleId} className="dialog" onClick={(e) => e.stopPropagation()}>
        <h2 id={titleId}>{title}</h2>
        <div className="dialog-body">{children}</div>
        <div className="actions">
          <button type="button" ref={cancel} onClick={onCancel}>
            Cancel
          </button>
          <button type="button" className={danger ? 'danger' : 'primary'} disabled={busy} onClick={onConfirm}>
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
