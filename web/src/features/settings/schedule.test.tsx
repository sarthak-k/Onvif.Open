import { fireEvent, screen, within } from '@testing-library/react';
import { renderApp } from '../../test/renderApp';
import { admin, policies, tenancyRoutes, tokens } from '../../test/fakeRecorder';
import { businessHours, describeDays, describeSchedule, scheduleProblem } from './schedule';

const shopHours = {
  ...policies[1],
  id: 'p-shop',
  name: 'Shop hours',
  mode: 'Schedule' as const,
  isDefault: false,
  schedule: [{ days: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'] as const, start: '08:00', end: '18:00' }],
};

describe('recording schedules', () => {
  it('describes hours in short words', () => {
    expect(describeDays(['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'])).toBe('Mon–Fri');
    expect(describeDays(['Monday', 'Wednesday', 'Saturday', 'Sunday'])).toBe('Mon, Wed, Sat, Sun');
    expect(describeDays(['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'])).toBe('Every day');
    expect(describeSchedule([...businessHours, { days: ['Friday'], start: '22:00', end: '06:00' }])).toBe('Mon–Fri 08:00–18:00; Fri 22:00–06:00 (next day)');
    expect(scheduleProblem([])).toBe('Add at least one time range.');
    expect(scheduleProblem([{ days: [], start: '08:00', end: '18:00' }])).toBe('Each time range needs at least one day.');
    expect(scheduleProblem([{ days: ['Monday'], start: '09:00', end: '09:00' }])).toBe('A time range cannot start and end at the same time.');
    expect(scheduleProblem(businessHours)).toBeNull();
  });

  it('lets administrators set the hours of a policy', async () => {
    let created: unknown;
    const { user, client } = renderApp('/settings/policies', {
      ...tenancyRoutes,
      'POST /api/auth/refresh': () => ({ body: tokens(1) }),
      'GET /api/auth/me': () => ({ body: admin }),
      'GET /api/recording-policies': () => ({ body: [...policies, shopHours] }),
      'POST /api/recording-policies': ({ body }) => {
        created = body;
        return { status: 201, body: { id: 'p-new', ...(body as object) } };
      },
    });

    const row = (await screen.findByText('Shop hours')).closest('tr')!;
    expect(within(row).getByText(/Records always Mon–Fri 08:00–18:00, otherwise around events/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Add policy' }));
    const dialog = screen.getByRole('dialog');
    await user.type(within(dialog).getByLabelText('Name'), 'Weekend shop');
    await user.click(within(dialog).getByLabelText('Record always during set hours, around events otherwise'));

    // Business hours to start from: weekdays 08:00–18:00. Saturday added, closing at 17:00.
    const first = within(dialog).getByRole('group', { name: 'Time range 1' });
    expect(within(first).getByLabelText('Monday')).toBeChecked();
    expect(within(first).getByLabelText('Saturday')).not.toBeChecked();
    await user.click(within(first).getByLabelText('Saturday'));
    fireEvent.change(within(first).getByLabelText('To'), { target: { value: '17:00' } });

    // A second range, then removed again; with no day left the dialog says why it cannot save.
    await user.click(within(dialog).getByRole('button', { name: 'Add hours' }));
    const second = within(dialog).getByRole('group', { name: 'Time range 2' });
    await user.click(within(second).getByLabelText('Saturday'));
    expect(within(dialog).getByText('Each time range needs at least one day.')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Save' })).toBeDisabled();
    await user.click(within(second).getByRole('button', { name: 'Remove' }));

    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    await vi.waitFor(() =>
      expect(created).toMatchObject({
        name: 'Weekend shop',
        mode: 'Schedule',
        schedule: [{ days: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'], start: '08:00', end: '17:00' }],
      }),
    );
    client.signOut();
  });
});
