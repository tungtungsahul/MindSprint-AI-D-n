const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const apiSource = fs.readFileSync(path.join(__dirname, '../frontend/api.js'), 'utf8');

function createApi(runtimeConfig, cachedBase) {
    const calls = [];
    const window = {
        location: {hostname: '127.0.0.1', origin: 'http://127.0.0.1:8080'},
        MINDSPRINT_CONFIG: runtimeConfig
    };
    const localStorage = {
        getItem(key) { return key === 'api-base' ? cachedBase : null; },
        setItem() {}, removeItem() {}
    };
    const fetch = async url => {
        calls.push(String(url));
        return {ok: true, status: 200, json: async () => ({id: 1})};
    };
    vm.runInNewContext(apiSource, {window, localStorage, fetch, URLSearchParams});
    return {api: window.MindSprintApi, calls};
}

test('self-host same-origin API ignores a cached old API port', async () => {
    const {api, calls} = createApi({apiBase: 'http://127.0.0.1:8080', sameOrigin: true}, 'http://localhost:5000');

    assert.equal(api.getApiBase(), '');
    await api.getMe();
    assert.deepEqual(calls, ['/api/auth/me']);
});

test('cloud deployment continues using its configured API origin', async () => {
    const {api, calls} = createApi({apiBase: 'https://api.example.test', sameOrigin: false}, null);

    await api.getMe();
    assert.deepEqual(calls, ['https://api.example.test/api/auth/me']);
});