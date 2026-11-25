// node tests/MindSprintAI_ASP.NET_MVC.Tests/autosave.check.cjs
// Chạy script thật với DOM/HTTP giả lập; không thay thế kiểm nghiệm trình duyệt và SQL.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../wwwroot/js/autosave.js'), 'utf8');
class Element {
    constructor(tag = 'DIV', type = '') {
        this.tagName = tag; this.type = type; this.children = []; this.attrs = {}; this.events = {};
        this.classes = new Set(); this.disabled = false; this.hidden = false; this.text = '';
    }
    get classList() { return { add: x => this.classes.add(x), remove: x => this.classes.delete(x), toggle: (x, on) => on ? this.classes.add(x) : this.classes.delete(x) }; }
    setAttribute(k, v) { this.attrs[k] = v; }
    getAttribute(k) { return this.attrs[k]; }
    removeAttribute(k) { delete this.attrs[k]; }
    hasAttribute(k) { return Object.hasOwn(this.attrs, k); }
    addEventListener(k, f) { this.events[k] = f; }
    trigger(k, target = this) { this.events[k]?.({ target, preventDefault() {} }); }
    append(...items) { this.children.push(...items); }
    replaceChildren(...items) { this.children = items; this.text = ''; }
    set textContent(value) { this.text = value; this.children = []; }
    get textContent() { return this.text + this.children.map(c => c.textContent).join(''); }
    matches() { return this.disabled || this.fieldsetDisabled; }
    closest() { return null; }
}
function setup({ confirm = false, reload = false, supported = true } = {}) {
    const checkbox = Object.assign(new Element('INPUT', 'checkbox'), { checked: false, name: 'enabled', value: 'true' });
    const radio1 = Object.assign(new Element('INPUT', 'radio'), { checked: true, name: 'policy', value: '1' });
    const radio2 = Object.assign(new Element('INPUT', 'radio'), { checked: false, name: 'policy', value: '2' });
    const select = Object.assign(new Element('SELECT', 'select-multiple'), { name: 'choices', options: [{ selected: true }, { selected: false }] });
    const locked = Object.assign(new Element('INPUT', 'checkbox'), { checked: true, name: 'locked', disabled: true });
    const hidden = Object.assign(new Element('INPUT', 'hidden'), { name: 'enabled', value: 'false' });
    const token = Object.assign(new Element('INPUT', 'hidden'), { name: '__RequestVerificationToken', value: 'test-only' });
    const submit = new Element('BUTTON', 'submit');
    const controls = [checkbox, radio1, radio2, select, locked];
    const form = Object.assign(new Element('FORM'), {
        action: 'http://localhost/Settings/Save/9', dataset: { autosave: reload ? 'reload' : '' },
        elements: [...controls, hidden, token, submit], valid: true,
        reportValidity() { return this.valid; }, querySelectorAll(selector) { return selector === '[data-autosave-summary]' ? [] : controls; }
    });
    if (confirm) form.setAttribute('data-confirm', 'Xác nhận');
    let now = 0, nextId = 0, reloads = 0, reply = {}, networkError = false, pending;
    const timers = new Map(), calls = [], cards = new Element();
    const context = {
        URL, document: {
            querySelectorAll: () => [form], createElement: tag => new Element(tag.toUpperCase()),
            createElementNS: (_, tag) => new Element(tag.toUpperCase()), getElementById: () => cards
        },
        FormData: class {
            constructor(f) {
                this.fields = f.elements.filter(c => c.name && !c.disabled && !c.fieldsetDisabled && (!['checkbox', 'radio'].includes(c.type) || c.checked))
                    .map(c => [c.name, c.tagName === 'SELECT' ? c.options.map(o => o.selected) : c.value]);
            }
        },
        DOMParser: class {
            parseFromString(text) {
                const data = JSON.parse(text);
                return { querySelectorAll: selector => selector.startsWith('.alert-danger')
                    ? (data.errors || []).map(textContent => ({ textContent }))
                    : data.login ? [] : [{ getAttribute: () => form.action, querySelectorAll: () => [] }] };
            }
        },
        fetch: (url, options) => {
            calls.push({ url, options });
            return new Promise((resolve, reject) => pending = () => networkError ? reject(new TypeError('Network failure')) : resolve({
                ok: reply.ok !== false, url: form.action, text: async () => JSON.stringify(reply)
            }));
        },
        setTimeout: (f, delay) => { const id = ++nextId; timers.set(id, { f, at: now + delay }); return id; },
        clearTimeout: id => timers.delete(id)
    };
    context.window = { fetch: supported, FormData: true, DOMParser: true, location: { reload: () => reloads++ } };
    vm.runInNewContext(source, context);
    const flush = async () => { for (let i = 0; i < 12; i++) await Promise.resolve(); };
    return {
        form, checkbox, radio1, radio2, select, locked, submit, cards, calls,
        get status() { return form.children[0]; }, get reloads() { return reloads; },
        change(control = checkbox) { form.trigger('change', control); },
        async advance(ms) {
            now += ms;
            for (const [id, timer] of [...timers]) if (timer.at <= now) { timers.delete(id); timer.f(); }
            await flush();
        },
        async finish(response = {}, network = false) { reply = response; networkError = network; pending(); await flush(); }
    };
}
(async () => {
    let checks = 0;
    const check = (value, message) => { assert.ok(value, message); checks++; };
    for (const options of [{ confirm: true }, { supported: false }]) {
        const t = setup(options);
        check(!t.submit.hidden && !t.status, 'Giữ nút khi thiếu JS hoặc có xác nhận');
    }
    const t = setup();
    check(t.submit.hidden, 'Ẩn submit khi có JS');
    check(t.status.attrs.role === 'status' && t.status.attrs['aria-live'] === 'polite', 'Thông báo đọc màn hình');
    t.checkbox.checked = true; t.change(); await t.advance(200);
    t.radio1.checked = false; t.radio2.checked = true; t.select.options[1].selected = true; t.change(t.select);
    await t.advance(399); check(t.calls.length === 0, 'Gộp thay đổi nhanh');
    await t.advance(1);
    check(t.calls.length === 1 && t.checkbox.disabled && t.locked.disabled, 'Khóa điều khiển lúc gửi');
    check(t.status.textContent === 'Đang lưu…', 'Trạng thái đang lưu');
    const request = t.calls[0];
    check(request.options.method === 'POST' && request.options.credentials === 'same-origin', 'POST cùng nguồn');
    check(request.options.body.fields.some(([k]) => k === '__RequestVerificationToken'), 'Giữ antiforgery');
    check(request.options.body.fields.filter(([k]) => k === 'enabled').length === 2, 'Giữ checkbox true trước hidden false');
    t.change(); t.form.trigger('submit'); await t.advance(400);
    check(t.calls.length === 1, 'Không gửi trùng lúc đang gửi');
    await t.finish();
    check(!t.checkbox.disabled && t.locked.disabled, 'Khôi phục trạng thái disabled ban đầu');
    check(t.status.textContent === 'Đã lưu', '200 HTML hợp lệ thành công');
    await t.advance(1999); check(!t.status.classes.has('text-muted'), 'Chưa mờ trước 2 giây');
    await t.advance(1); check(t.status.classes.has('text-muted'), 'Mờ sau 2 giây');
    t.checkbox.checked = false; t.radio1.checked = true; t.radio2.checked = false; t.select.options[1].selected = false;
    t.change(); await t.advance(400);
    await t.finish({}, true);
    check(t.checkbox.checked && !t.radio1.checked && t.radio2.checked && t.select.options[1].selected, 'Lỗi mạng trả checkbox/radio/select về bản lưu mới nhất');
    check(t.status.textContent === 'Chưa lưu được. Thử lạiThử lại', 'Lỗi mạng dùng chữ G0');
    t.status.children.at(-1).trigger('click');
    check(t.calls.length === 3 && !t.checkbox.checked, 'Thử lại gửi giá trị đã chọn, không gửi bản cũ');
    await t.finish();
    check(!t.checkbox.checked && t.status.textContent === 'Đã lưu', 'Thử lại thành công cập nhật bản lưu');
    for (const response of [{ errors: ['Không được đổi cài đặt này.'] }, { errors: ['<script>không chạy</script>'] }, { errors: [''] }, { ok: false }, { login: true }]) {
        t.checkbox.checked = true; t.change(); await t.advance(400); await t.finish(response);
        check(!t.checkbox.checked && t.status.textContent.includes('Chưa lưu được'), 'HTML lỗi/HTTP lỗi/đăng nhập 200 đều trả giá trị cũ');
        if (response.errors?.[0]) check(t.status.textContent.includes(response.errors[0]), 'Giữ đúng chữ lỗi máy chủ bằng textContent');
    }
    t.checkbox.checked = true; t.change(); await t.advance(400); await t.finish();
    check(t.checkbox.checked, 'Đổi mới thay thế yêu cầu thử lại cũ');
    const locked = setup(); locked.checkbox.fieldsetDisabled = true; locked.change(); await locked.advance(400);
    check(locked.calls.length === 0, 'Không gửi từ fieldset bị khóa');
    const invalid = setup(); invalid.form.valid = false; invalid.change(); await invalid.advance(400);
    check(invalid.calls.length === 0, 'Không gửi khi form không hợp lệ');
    const reload = setup({ reload: true }); reload.change(); await reload.advance(400); await reload.finish();
    check(reload.cards.hasAttribute('inert') && reload.reloads === 0, 'Ngừng tương tác thẻ cũ khi chờ tải chế độ mới');
    reload.change(); reload.form.trigger('submit'); await reload.advance(400);
    check(reload.checkbox.disabled && reload.calls.length === 1, 'Không hủy lịch tải lại bằng thay đổi tiếp theo');
    await reload.advance(1600); check(reload.reloads === 1, 'Tải lại danh sách học thẻ sau thành công');
    console.log(`${checks} kiểm tra autosave DOM/HTTP giả lập qua.`);
})().catch(error => { console.error(error); process.exitCode = 1; });
