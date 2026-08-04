import { defineConfig } from '@playwright/test';

/**
 * No webServer here on purpose: the host is started separately (see CONTRIBUTING.md), and this
 * config only points at whatever URL is already serving it.
 */
export default defineConfig({
  testDir: '.',
  use: {
    baseURL: process.env.BCF_BASE_URL ?? 'http://localhost:5000',
  },
});
