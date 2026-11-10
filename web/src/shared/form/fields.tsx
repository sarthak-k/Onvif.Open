import { useId, type InputHTMLAttributes, type ReactNode } from 'react';

// Labelled inputs: the label wraps the control, so every field is reachable by its label (accessibility and tests).

type InputProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange' | 'type'>;

interface TextFieldProps extends InputProps {
  label: string;
  value: string;
  onChange(value: string): void;
  type?: 'text' | 'password' | 'url';
}

export function TextField({ label, value, onChange, type = 'text', ...input }: TextFieldProps) {
  return (
    <label>
      {label}
      <input type={type} value={value} onChange={(e) => onChange(e.target.value)} {...input} />
    </label>
  );
}

export function NumberField({ label, value, onChange, step = 'any', ...input }: Omit<TextFieldProps, 'type'>) {
  return (
    <label>
      {label}
      <input type="number" min="0" step={step} value={value} onChange={(e) => onChange(e.target.value)} {...input} />
    </label>
  );
}

export function TextAreaField({ label, value, onChange, rows = 3, placeholder }: { label: string; value: string; onChange(value: string): void; rows?: number; placeholder?: string }) {
  return (
    <label>
      {label}
      <textarea rows={rows} value={value} placeholder={placeholder} onChange={(e) => onChange(e.target.value)} />
    </label>
  );
}

export function CheckboxField({ label, checked, onChange, ...input }: Omit<InputProps, 'checked'> & { label: ReactNode; checked: boolean; onChange(checked: boolean): void }) {
  return (
    <label className="check">
      <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} {...input} />
      {label}
    </label>
  );
}

export interface Option<T extends string> {
  value: T;
  label: string;
}

export function SelectField<T extends string>({ label, value, options, onChange, disabled }: { label: string; value: T; options: readonly Option<T>[]; onChange(value: T): void; disabled?: boolean }) {
  return (
    <label>
      {label}
      <select value={value} disabled={disabled} onChange={(e) => onChange(e.target.value as T)}>
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </label>
  );
}

export function RadioGroup<T extends string>({ legend, value, options, onChange }: { legend: string; value: T; options: readonly Option<T>[]; onChange(value: T): void }) {
  const name = useId();
  return (
    <fieldset className="choice">
      <legend>{legend}</legend>
      {options.map((option) => (
        <label key={option.value} className="check">
          <input type="radio" name={name} checked={value === option.value} onChange={() => onChange(option.value)} />
          {option.label}
        </label>
      ))}
    </fieldset>
  );
}

/** A titled group of fields. */
export function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <fieldset className="card stack">
      <legend>{title}</legend>
      {children}
    </fieldset>
  );
}
