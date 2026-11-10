import { expect, test } from '@playwright/test';
import { e2e } from '../playwright.config';

test.beforeEach(async ({ page }) => {
  await page.route('**/config.json', (route) => route.fulfill({ json: { apiBaseUrl: e2e.recorderUrl } }));
});

test('an alert rule catches a camera going offline, and it is acknowledged', async ({ page }) => {
  await page.goto('/');
  await page.getByLabel('User name').fill('admin');
  await page.getByLabel('Password', { exact: true }).fill(e2e.adminPassword);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Home' })).toBeVisible();

  // A rule for cameras going offline, shown in the inbox only.
  await page.getByRole('link', { name: 'Settings' }).click();
  await page.getByRole('link', { name: 'Alert rules' }).click();
  await page.getByRole('button', { name: 'Add rule' }).click();
  const form = page.getByRole('form', { name: 'Alert rule' });
  await form.getByLabel('Name', { exact: true }).fill('Cameras offline');
  await form.getByLabel('Person', { exact: true }).uncheck();
  await form.getByLabel('Camera offline', { exact: true }).check();
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('row', { name: /Cameras offline/ })).toContainText('Inbox only');

  // A recorded camera nobody answers for: the recorder reports it offline, and the rule raises an alert.
  await page.goto('/cameras/new');
  await page.getByLabel('Name', { exact: true }).fill('Back door');
  await page.getByLabel('RTSP stream only').check();
  await page.getByLabel('RTSP URL').fill('rtsp://127.0.0.1:9/back');
  await page.getByRole('button', { name: 'Add camera' }).click();
  await expect(page.getByRole('link', { name: 'Back door', exact: true })).toBeVisible();

  await page.getByRole('link', { name: 'Alerts', exact: true }).click();
  const alert = page.getByRole('listitem', { name: 'Camera offline at Back door' }).first();
  await expect(async () => {
    await page.reload();
    await expect(alert).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout: 60_000 });
  await expect(alert).toContainText('Rule: Cameras offline');
  await expect(page.getByRole('navigation', { name: 'Main' }).getByLabel(/\d+ open/)).toBeVisible();

  await page.getByLabel('Show').selectOption({ label: 'All alerts' });
  await alert.getByRole('button', { name: 'Acknowledge' }).click();
  await expect(alert).toContainText('Acknowledged by admin');

  // Who acknowledged it is in the audit log.
  await page.goto('/settings/audit');
  await page.getByLabel('Show').selectOption('alert');
  await expect(page.getByRole('row', { name: /Acknowledged an alert/ })).toBeVisible();
  await expect(page.getByRole('row', { name: /Added an alert rule.*Cameras offline/ })).toBeVisible();

  // Leave the recorder as it was for the other tests.
  await page.getByRole('link', { name: 'Cameras', exact: true }).click();
  await page.getByRole('row', { name: /Back door/ }).getByRole('button', { name: 'Delete' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Delete' }).click();
  await expect(page.getByText('No cameras yet.')).toBeVisible();
  await page.goto('/settings/alerts');
  await page.getByRole('row', { name: /Cameras offline/ }).getByRole('button', { name: 'Delete' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Delete' }).click();
  await expect(page.getByText(/No rules yet/)).toBeVisible();
});
