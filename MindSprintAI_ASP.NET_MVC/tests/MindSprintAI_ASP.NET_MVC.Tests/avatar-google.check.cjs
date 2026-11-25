// DOM giả lập: chạy mã thật, không thay thế thử trình duyệt.
const fs = require('node:fs'), vm = require('node:vm'), assert = require('node:assert/strict');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../wwwroot/js/avatar.js'), 'utf8');
const cached = { complete: true, naturalWidth: 0, hidden: false };
const loaded = { complete: true, naturalWidth: 40, hidden: false };
const pending = { complete: false, naturalWidth: 0, hidden: false };
let handler;
vm.runInNewContext(source, { document: {
    addEventListener: (name, fn, capture) => {
        assert.equal(name, 'error'); assert.equal(capture, true); handler = fn;
    },
    querySelectorAll: () => [cached, loaded, pending]
} });
assert.equal(cached.hidden, true);
assert.equal(loaded.hidden, false);
assert.equal(pending.hidden, false);
pending.matches = selector => selector === '.avatar-img';
handler({ target: pending });
assert.equal(pending.hidden, true);
const unrelated = { hidden: false, matches: () => false };
handler({ target: unrelated });
assert.equal(unrelated.hidden, false);
console.log('Avatar fallback: 7 checks passed');
