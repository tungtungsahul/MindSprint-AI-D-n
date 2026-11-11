// Run against the real Docker stack: node tests/selfhost-browser.cjs
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const path = require('node:path');
const { launchBrowser } = require('./playwright-launch.cjs');
const { chromium } = require(path.join(__dirname, '../backend/MindSprint.Api/bin/Release/net8.0/.playwright/package'));

(async () => {
    const browser = await launchBrowser(chromium);
    const context = await browser.newContext({ serviceWorkers: 'block' });
    const base = process.env.MINDSPRINT_BROWSER_URL || 'http://localhost:8080';
    const email = `hosting-browser-${crypto.randomBytes(8).toString('hex')}@example.com`;
    const password = crypto.randomBytes(12).toString('hex');
    try {
        const registration = await context.request.post(base + '/api/auth/register', {
            data: { email, password, displayName: 'Self-host browser check' }
        });
        assert.equal(registration.status(), 200);
        const page = await context.newPage();
        const errors = [];
        const serverFailures = [];
        const apiResponses = new Map();
        page.on('pageerror', error => errors.push(error.message));
        page.on('response', response => {
            if (response.url().startsWith(base + '/api/'))
                apiResponses.set(new URL(response.url()).pathname, response.status());
            if (response.url().startsWith(base + '/') && response.status() >= 500)
                serverFailures.push(`${response.status()} ${new URL(response.url()).pathname}`);
        });
        await page.goto(base, { waitUntil: 'networkidle' });
        assert.equal(new URL(page.url()).origin, new URL(base).origin, 'The browser must retain the short hostname');
        await page.locator('#sidebar-auth [data-act=login]').click();
        assert.equal(await page.locator('[data-act=demo]').count(), 0);
        await page.locator('#auth-email').fill(email);
        await page.locator('#auth-pass').fill(password);
        const login = page.waitForResponse(response => new URL(response.url()).pathname === '/api/auth/login');
        await page.locator('#auth-submit-btn').click();
        assert.equal((await login).status(), 200);
        await page.locator('.auth-overlay').waitFor({ state: 'detached' });
        assert.equal(await page.locator('#sidebar-user-name').innerText(), 'Self-host browser check');
        await page.reload({ waitUntil: 'networkidle' });
        assert.equal(await page.locator('#sidebar-user-name').innerText(), 'Self-host browser check');
        for (const [tab, endpoint] of [['kanban', '/api/tasks'], ['notebook', '/api/notebooks']]) {
            const loaded = apiResponses.has(endpoint) ? Promise.resolve(apiResponses.get(endpoint))
                : page.waitForResponse(response => new URL(response.url()).pathname === endpoint).then(response => response.status());
            await page.locator(`.sidebar [data-tab=${tab}]`).click();
            assert.equal(await loaded, 200);
        }
        const logout = page.waitForResponse(response => new URL(response.url()).pathname === '/api/auth/revoke');
        await page.locator('#sidebar-auth [data-act=logout]').click();
        assert.equal((await logout).status(), 200);
        assert.deepEqual(errors, []);
        assert.deepEqual(serverFailures, []);
        console.log('PASS: real-browser email login, reload, Kanban/notebook API access and logout; no application errors.');
        console.log('One isolated hosting-browser account was created. Google account consent remains a manual check.');
    } finally {
        await context.close();
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
