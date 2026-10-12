const { defineConfig } = require('@playwright/test');
const pathBase = process.env.WEBBOARD_BROWSER_PATH_BASE || '';
const port = process.env.WEBBOARD_BROWSER_PROXY_PORT || 19443;
module.exports = defineConfig({
    testDir: '.', testMatch: '*.spec.cjs', workers: 1, timeout: 60000,
    use: { trace: 'retain-on-failure', screenshot: 'only-on-failure', baseURL: `https://localhost:${port}`, ignoreHTTPSErrors: true,
        launchOptions: process.env.WEBBOARD_CHROMIUM_EXECUTABLE ? { executablePath: process.env.WEBBOARD_CHROMIUM_EXECUTABLE } : {} },
    webServer: { command: 'node server.cjs', url: `https://localhost:${port}${pathBase}/Login`, ignoreHTTPSErrors: true, timeout: 60000, reuseExistingServer: false }
});
