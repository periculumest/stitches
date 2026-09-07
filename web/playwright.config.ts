import { defineConfig } from '@playwright/test';
export default defineConfig({ testDir: './tests', workers: 1, timeout: 45000, use: { baseURL: process.env.STITCH_TEST_URL || 'http://127.0.0.1:5057', viewport: { width: 1440, height: 1000 }, channel: 'chrome', headless: true, screenshot: 'only-on-failure', trace: 'retain-on-failure' }, reporter: [['list']], });
