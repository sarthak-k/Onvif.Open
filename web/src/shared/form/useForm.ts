import { useCallback, useState } from 'react';

/** Sets one field of a form's values. */
export type FieldSetter<T> = <K extends keyof T>(key: K, value: T[K]) => void;

/** A form's values with a stable, typed per-field setter. */
export function useForm<T extends object>(initial: T) {
  const [values, setValues] = useState<T>(initial);
  const set = useCallback<FieldSetter<T>>((key, value) => setValues((current) => ({ ...current, [key]: value })), []);
  return { values, set, setValues };
}
