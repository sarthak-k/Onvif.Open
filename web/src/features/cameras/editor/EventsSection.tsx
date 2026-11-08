import { CheckboxField, NumberField, Section, TextAreaField, TextField } from '../../../shared/form/fields';
import type { SectionProps } from './cameraForm';

/** ONVIF events and Profile M detections; only for ONVIF cameras. */
export function EventsSection({ form, set }: SectionProps) {
  const triggered = form.mode === 'Triggered';
  return (
    <Section title="Events and analytics">
      <CheckboxField label="Receive camera events (motion, tampering, inputs)" checked={form.eventsEnabled} onChange={(v) => set('eventsEnabled', v)} />
      {form.eventsEnabled && triggered && (
        <TextAreaField
          label="Topics that trigger recording (one per line, empty: motion)"
          value={form.triggerTopics}
          onChange={(v) => set('triggerTopics', v)}
          placeholder="tns1:RuleEngine/CellMotionDetector/Motion"
        />
      )}
      <CheckboxField label="Receive object detections (Profile M)" checked={form.analyticsEnabled} onChange={(v) => set('analyticsEnabled', v)} />
      {form.analyticsEnabled && (
        <div className="row">
          {triggered && <TextField label="Classes that trigger recording" value={form.triggerClasses} onChange={(v) => set('triggerClasses', v)} placeholder="Human, Vehicle" />}
          <NumberField label="Minimum likelihood (0–1)" value={form.minLikelihood} onChange={(v) => set('minLikelihood', v)} step="0.05" />
          <TextField label="Analytics profile token" value={form.analyticsProfileToken} onChange={(v) => set('analyticsProfileToken', v)} placeholder="The recorded profile" />
        </div>
      )}
    </Section>
  );
}
