import { expect, test, type Page } from '@playwright/test';
import { e2e } from '../playwright.config';

// The built app reads /config.json for the recorder's URL; point it at the e2e recorder.
test.beforeEach(async ({ page }) => {
  await page.route('**/config.json', (route) => route.fulfill({ json: { apiBaseUrl: e2e.recorderUrl } }));
});

async function signIn(page: Page, userName: string, password: string) {
  await page.goto('/');
  await page.getByLabel('User name').fill(userName);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
}

test('an administrator manages sites and cameras', async ({ page }) => {
  await signIn(page, 'admin', e2e.adminPassword);
  await expect(page.getByRole('heading', { name: 'Cameras' })).toBeVisible();
  await expect(page.getByText('No cameras yet.')).toBeVisible();

  // A second site, in its own time zone.
  await page.getByRole('link', { name: 'Settings' }).click();
  await expect(page.getByRole('heading', { name: 'Sites' })).toBeVisible();
  await page.getByRole('button', { name: 'Add site' }).click();
  await page.getByRole('dialog').getByLabel('Name', { exact: true }).fill('Warehouse');
  await page.getByRole('dialog').getByLabel('Time zone').selectOption('Europe/Paris');
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('row', { name: /Warehouse/ })).toContainText('Europe/Paris');

  await page.getByRole('link', { name: 'Cameras', exact: true }).click();
  await page.getByRole('link', { name: 'Add camera' }).click();
  await page.getByLabel('Name', { exact: true }).fill('Gate');
  await page.getByLabel('Site').selectOption({ label: 'Warehouse' });
  await page.getByLabel('RTSP stream only').check();
  // Nothing listens on port 9: the test reports the camera unreachable.
  await page.getByLabel('RTSP URL').fill('rtsp://127.0.0.1:9/stream');
  await page.getByRole('button', { name: 'Test connection' }).click();
  await expect(page.getByRole('alert')).toContainText('cannot be reached');

  await page.getByLabel('User name').fill('viewer');
  await page.getByLabel('Password', { exact: true }).fill('camera-secret');
  await page.getByLabel('Record this camera').uncheck();
  await page.getByRole('button', { name: 'Add camera' }).click();

  const row = page.getByRole('row', { name: /Gate/ });
  await expect(row).toContainText('Warehouse');
  await expect(row).toContainText('RTSP 127.0.0.1:9');
  await expect(row).toContainText('disabled');
  await expect(row).toContainText('Continuous');

  // Edit: renamed, recording only around events; the stored password is kept.
  await row.getByRole('link', { name: 'Gate' }).click();
  await expect(page.getByLabel('Password', { exact: true })).toHaveAttribute('placeholder', 'Unchanged');
  await page.getByLabel('Name', { exact: true }).fill('Loading gate');
  await page.getByLabel('Recording policy').selectOption({ label: 'Events only' });
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('row', { name: /Loading gate/ })).toContainText('Triggered');

  await page.getByRole('link', { name: 'Loading gate' }).click();
  await expect(page.getByLabel('Recording policy')).toHaveValue(/.+/);
  await expect(page.getByText(/Records around events/)).toBeVisible();
  await expect(page.getByLabel('Remove the stored password')).toBeVisible();
  await page.getByRole('link', { name: 'Back to cameras' }).click();

  // A site with cameras cannot be deleted; once empty, it can.
  await page.getByRole('row', { name: /Loading gate/ }).getByRole('button', { name: 'Delete' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Delete' }).click();
  await expect(page.getByText('No cameras yet.')).toBeVisible();

  await page.getByRole('link', { name: 'Settings' }).click();
  await page.getByRole('row', { name: /Warehouse/ }).getByRole('button', { name: 'Delete' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Delete' }).click();
  await expect(page.getByRole('row', { name: /Warehouse/ })).toHaveCount(0);

  // All of it is in the audit log, newest first.
  await page.getByRole('link', { name: 'Audit log' }).click();
  await expect(page.getByRole('row', { name: /Removed a site.*Warehouse/ })).toBeVisible();
  await expect(page.getByRole('row', { name: /Removed a camera.*Loading gate/ })).toBeVisible();
  await expect(page.getByRole('row', { name: /Changed a camera.*Loading gate/ })).toBeVisible();
  await expect(page.getByRole('row', { name: /Added a camera.*Gate/ })).toBeVisible();
  await page.getByLabel('Show').selectOption('auth');
  await expect(page.getByRole('row', { name: /Signed in/ }).first()).toBeVisible();
  await expect(page.getByRole('row', { name: /a site/ })).toHaveCount(0);
});

test('a new user chooses a password and sees only their pages', async ({ page }) => {
  await signIn(page, 'admin', e2e.adminPassword);
  await page.getByRole('link', { name: 'Settings' }).click();
  await page.getByRole('link', { name: 'Users' }).click();

  const form = page.getByRole('form', { name: 'Add user' });
  await form.getByLabel('User name').fill('olivia');
  await form.getByLabel('Initial password').fill('Initial-Password-1');
  await form.getByLabel('Role').selectOption('Operator');
  await form.getByRole('button', { name: 'Add user' }).click();
  await expect(page.getByRole('row', { name: /olivia/ })).toContainText('must change password');

  await page.getByRole('button', { name: 'Sign out' }).click();
  await signIn(page, 'olivia', 'Initial-Password-1');

  await expect(page.getByRole('heading', { name: 'Choose a new password' })).toBeVisible();
  await page.getByLabel('Current password').fill('Initial-Password-1');
  await page.getByLabel('New password', { exact: true }).fill('Olivia-Own-Password-2');
  await page.getByLabel('Repeat new password').fill('Olivia-Own-Password-2');
  await page.getByRole('button', { name: 'Change password' }).click();

  await expect(page.getByRole('heading', { name: 'Cameras' })).toBeVisible();
  await page.getByRole('link', { name: 'Settings' }).click();
  await expect(page.getByRole('heading', { name: 'Organization' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Users' })).toHaveCount(0);
  await page.getByRole('link', { name: 'Cameras', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Add camera' })).toHaveCount(0);

  // The session survives a reload only with "keep me signed in" or in the same tab.
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Cameras' })).toBeVisible();

  await page.getByRole('button', { name: 'Sign out' }).click();
  await signIn(page, 'olivia', 'Initial-Password-1');
  await expect(page.getByRole('alert')).toContainText('Wrong user name or password');
});
