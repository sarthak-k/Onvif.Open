import { defineConfig, devices } from '@playwright/test';
import path from 'node:path';

// The app (vite preview, :4173) and a real recorder (:5081) on different origins, as deployed:
// the e2e tests also cover CORS and bearer tokens. The recorder starts with an empty SQLite database.
const recorderUrl = 'http://localhost:5081';
const data = path.resolve(import.meta.dirname, 'e2e/.recorder');
const recorderProject = path.resolve(import.meta.dirname, '../Onvif.Open/Onvif.Open.Recorder');

export const e2e = { recorderUrl, adminPassword: 'E2e-Admin-Password-1' };

export default defineConfig({
  testDir: 'e2e',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: { baseURL: 'http://localhost:4173', trace: 'retain-on-failure' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      // A new database on every run.
      command: `node -e "require('fs').rmSync(process.argv[1], { recursive: true, force: true })" "${data}" && dotnet run --project "${recorderProject}" --no-launch-profile ${process.env.CI ? '--configuration Release' : ''}`,
      url: `${recorderUrl}/api/auth/me`,
      timeout: 180_000,
      reuseExistingServer: false,
      env: {
        Urls: recorderUrl,
        Database__Provider: 'Sqlite',
        Database__ConnectionString: `Data Source=${data}/e2e.db`,
        DataProtection__KeysDirectory: `${data}/keys`,
        Recording__StorageDirectory: `${data}/recordings`,
        Plugins__Directory: `${data}/plugins`,
        Security__InitialAdmin__UserName: 'admin',
        Security__InitialAdmin__Password: e2e.adminPassword,
        Security__AllowedOrigins__0: 'http://localhost:4173',
        Security__SignInsPerMinute: '100',
        // A camera without video answers the live playlist with 503 after this long.
        LiveView__StartTimeout: '00:00:02',
      },
    },
    {
      command: 'npm run build && npx vite preview --port 4173 --strictPort',
      url: 'http://localhost:4173',
      timeout: 120_000,
      reuseExistingServer: false,
    },
  ],
});
