const { test, expect } = require('@playwright/test');
const password = 'Browser-tests-password-2026';
async function register(page, name) {
    await page.goto('/Register');
    await page.locator('#Input_UserName').fill(name);
    await page.locator('#Input_Password').fill(password);
    await page.locator('#Input_ConfirmPassword').fill(password);
    await page.getByRole('button', { name: 'Create account', exact: true }).click();
    await expect(page).toHaveURL(/\/$/);
}
async function openPermissions(page, name) {
    await page.getByRole('button', { name: `Actions for ${name}`, exact: true }).click();
    await page.locator(`.js-edit-access[data-user-name="${name}"]`).click();
}
async function editPermissions(page, name, resource, read) {
    await page.goto('/UserManagement');
    await openPermissions(page, name);
    await expect(page.locator('#access-save')).toBeEnabled();
    await page.getByRole('checkbox', { name: `Read ${resource}`, exact: true }).setChecked(read);
    await page.locator('#access-audit-comment').fill('Browser permission check');
    await page.locator('#access-save').click();
    await expect(page.locator('.alert-success')).toContainText('CRUD access updated');
}
test('HTTPS cookies, module editing, permission races and immediate session revocation', async ({ page, browser, baseURL }) => {
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    await register(page, 'browser-admin');
    expect((await page.context().cookies()).find(cookie => cookie.name === 'webboard_session').secure).toBe(true);
    await page.goto('/ModuleManagement');
    await page.getByRole('button', { name: 'Add module', exact: true }).click();
    const type = page.getByRole('combobox', { name: 'Type', exact: true });
    await type.click(); await type.fill('Http'); await page.getByRole('option', { name: 'Http', exact: true }).click();
    await page.locator('#Module_Name').fill('browser-http');
    await page.locator('#Module_Description').fill('Created in a real browser');
    await page.locator('#Module_HealthCheckUrl').fill('https://example.com/health');
    await page.locator('#Module_ManagementUrl').fill('https://example.com');
    await page.locator('#save-module').click();
    await expect(page.locator('.alert-success')).toContainText('Module added');
    await page.getByRole('button', { name: 'Edit module', exact: true }).click();
    await expect(page.locator('#Module_Name')).toHaveValue('browser-http');
    await expect(page.locator('#Module_HealthCheckUrl')).toHaveValue('https://example.com/health');
    await page.locator('#Module_Description').fill('Edited in a real browser');
    await page.locator('#module-audit-comment').fill('Browser edit');
    await page.locator('#save-module').click();
    await expect(page.locator('.alert-success')).toContainText('Module updated');
    await page.goto('/'); await expect(page.getByText('Edited in a real browser')).toBeVisible();
    await page.goto('/MonitoringHistory'); await expect(page.locator('canvas')).toHaveCount(1);
    const contextA = await browser.newContext({ baseURL, ignoreHTTPSErrors: true });
    const contextB = await browser.newContext({ baseURL, ignoreHTTPSErrors: true });
    const a = await contextA.newPage(), b = await contextB.newPage();
    await register(a, 'browser-user-a'); await register(b, 'browser-user-b');
    await editPermissions(page, 'browser-user-a', 'Users', true);
    await editPermissions(page, 'browser-user-b', 'Modules', true);
    await page.goto('/UserManagement');
    const buttonA = page.locator('.js-edit-access[data-user-name="browser-user-a"]');
    const idA = await buttonA.getAttribute('data-user-id');
    let release, entered; const gate = new Promise(resolve => release = resolve), started = new Promise(resolve => entered = resolve);
    let finished; const completion = new Promise(resolve => finished = resolve);
    await page.route('**/UserManagement?**', async route => {
        const url = new URL(route.request().url());
        if (url.searchParams.get('handler') !== 'Access' || url.searchParams.get('userId') !== idA) return route.continue();
        const response = await route.fetch(); entered(); await gate;
        await route.fulfill({ response }).catch(() => {}); finished();
    });
    await openPermissions(page, 'browser-user-a'); await started;
    await page.locator('#access-modal .btn-close').click();
    await expect(page.locator('#access-modal')).toBeHidden();
    await openPermissions(page, 'browser-user-b');
    await expect(page.locator('#access-save')).toBeEnabled();
    await expect(page.locator('#access-modal-user')).toHaveText('browser-user-b');
    await expect(page.getByRole('checkbox', { name: 'Read Modules', exact: true })).toBeChecked();
    release(); await completion;
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    await expect(page.getByRole('checkbox', { name: 'Read Modules', exact: true })).toBeChecked();
    await expect(page.getByRole('checkbox', { name: 'Read Users', exact: true })).not.toBeChecked();
    await page.unroute('**/UserManagement?**');
    await page.locator('#access-modal .btn-close').click();
    expect((await a.goto('/Users')).status()).toBe(200);
    await editPermissions(page, 'browser-user-a', 'Users', false);
    expect((await a.goto('/Users')).status()).toBe(403);
    await page.goto('/UserManagement');
    await page.getByRole('button', { name: 'Actions for browser-user-b', exact: true }).click();
    page.once('dialog', dialog => dialog.accept());
    await page.getByRole('button', { name: 'Delete user', exact: true }).click();
    await expect(page.locator('.alert-success')).toContainText('User deleted');
    await b.goto('/ModuleManagement'); await expect(b).toHaveURL(/\/Login\?/);
    expect(errors).toEqual([]);
    await contextA.close(); await contextB.close();
});
