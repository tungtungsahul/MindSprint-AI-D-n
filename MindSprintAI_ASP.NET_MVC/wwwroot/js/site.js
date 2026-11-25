// Form có data-confirm hỏi lại trước khi gửi. Câu hỏi đọc từ thuộc tính (đã mã hóa HTML),
// không ghép chuỗi vào JavaScript, nên tên do người dùng đặt không chạy được thành mã.
// Dùng hộp Bootstrap thay confirm(): một số trình duyệt nhúng/webview tự trả "Hủy" mà không hiện hộp thoại.
(() => {
    let modal, pending;
    function dialog() {
        if (modal) return modal;
        const el = document.createElement('div');
        el.className = 'modal fade'; el.tabIndex = -1;
        el.setAttribute('aria-labelledby', 'confirm-dialog-text');
        el.innerHTML = '<div class="modal-dialog modal-dialog-centered"><div class="modal-content"><div class="modal-body"><p id="confirm-dialog-text" class="mb-0"></p></div>'
            + '<div class="modal-footer"><button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">Hủy</button><button type="button" class="btn btn-primary confirm-ok">Đồng ý</button></div></div></div>';
        document.body.append(el);
        modal = { el, box: new bootstrap.Modal(el) };
        el.querySelector('.confirm-ok').addEventListener('click', () => {
            let { form, submitter } = pending || {};
            pending = null; modal.box.hide();
            // Bảng tự làm mới (vd. bảng theo dõi bài) có thể thay form trong lúc hộp đang mở: gửi form mới cùng địa chỉ.
            if (form && !form.isConnected) {
                form = [...document.forms].find(f => f.action === form.action && f.method === form.method) || null;
                submitter = submitter?.name ? [...(form?.elements || [])].find(b => b.name === submitter.name && b.value === submitter.value) : null;
            }
            if (!form) return;
            form.dataset.confirmed = '1';
            // requestSubmit giữ giá trị nút đã bấm và chạy lại các trình xử lý submit khác (vd. play.js).
            form.requestSubmit(submitter && submitter.form === form ? submitter : undefined);
        });
        el.addEventListener('hidden.bs.modal', () => { pending = null; });
        return modal;
    }
    // Ô nhập sai nằm trong mục thu gọn (<details>) thì mở mục đó ra để trình duyệt chỉ được chỗ cần sửa.
    document.addEventListener('invalid', e => { const box = e.target.closest?.('details'); if (box) box.open = true; }, true);
    document.addEventListener('submit', e => {
        // Nút có data-confirm riêng (vd. chỉ nút Từ chối trong form Duyệt/Từ chối) thắng câu của form.
        const form = e.target, message = e.submitter?.dataset?.confirm || form.dataset?.confirm;
        if (!message) return;
        if (form.dataset.confirmed) { delete form.dataset.confirmed; return; }
        e.preventDefault();
        if (!window.bootstrap?.Modal) { if (confirm(message)) { form.dataset.confirmed = '1'; form.requestSubmit(e.submitter || undefined); } return; }
        const d = dialog();
        d.el.querySelector('#confirm-dialog-text').textContent = message; // chữ thuần, không chạy HTML
        pending = { form, submitter: e.submitter };
        d.el.addEventListener('shown.bs.modal', () => d.el.querySelector('.confirm-ok').focus(), { once: true });
        d.box.show();
    });
})();

(() => {
    const form = document.getElementById('assignment-form');
    if (!form) return;
    const mode = form.querySelector('#Mode');
    function update(resetShuffle) {
        const test = mode.value === '2';
        for (const [selector, visible] of [['[data-test-field]', test], ['[data-practice-field]', !test]]) {
            form.querySelectorAll(selector).forEach(group => {
                group.classList.toggle('d-none', !visible);
                group.querySelectorAll('input, select').forEach(input => input.disabled = !visible);
            });
        }
        form.querySelector('#DurationMinutes').required = test;
        if (resetShuffle && !form.dataset.practicePreset) {
            form.querySelector('#ShuffleQuestions').checked = test;
            form.querySelector('#ShuffleOptions').checked = test;
        }
    }
    mode.addEventListener('change', () => update(true));
    update(false);
})();

// Ô nhập dài có giới hạn (data-limit): đếm ký tự và chặn lưu khi vượt.
// Không dùng maxlength vì trình duyệt cắt bớt chữ dán vào mà không báo, người dùng lưu bản thiếu mà không biết.
document.querySelectorAll('textarea[data-limit]').forEach(box => {
    const limit = Number(box.dataset.limit), out = document.getElementById(box.dataset.limitOutput);
    const vn = n => n.toLocaleString('vi-VN');
    const update = () => {
        const over = box.value.length - limit;
        box.setCustomValidity(over > 0 ? `Văn bản dài hơn giới hạn ${vn(over)} ký tự. Hãy rút bớt rồi lưu lại.` : '');
        if (!out) return;
        out.textContent = over > 0 ? `Dài hơn giới hạn ${vn(over)} ký tự. Hãy rút bớt rồi lưu lại.` : `${vn(box.value.length)} / ${vn(limit)} ký tự`;
        out.classList.toggle('text-danger', over > 0);
    };
    box.addEventListener('input', update);
    update();
});

// Ô tìm trang ở header (như giao diện nhóm): gợi ý các trang có trong thanh bên, chọn tên trang thì chuyển tới. Không gọi máy chủ.
(() => {
    const form = document.querySelector('[data-app-search]');
    if (!form) return;
    const input = form.querySelector('input'), list = form.querySelector('datalist');
    const pages = new Map();
    document.querySelectorAll('.mindsprint-header a.nav-link[href], .account-menu a.dropdown-item[href]').forEach(a => {
        const name = (a.querySelector('.nav-label')?.textContent || a.textContent).trim();
        if (name && !pages.has(name)) pages.set(name, a.href);
    });
    for (const name of pages.keys()) list.append(new Option(name));
    // Như nhóm ("Tìm trang hoặc bộ thẻ"): thêm tên bộ học của mình + chủ đề từ vựng, lấy một lần khi bấm vào ô tìm lần đầu.
    if (form.dataset.itemsUrl) input.addEventListener('focus', async () => {
        try {
            const res = await fetch(form.dataset.itemsUrl, { headers: { Accept: 'application/json' } });
            if (!res.ok || !res.headers.get('content-type')?.includes('json')) return;
            for (const item of await res.json()) if (item.url && !pages.has(item.name)) { pages.set(item.name, item.url); list.append(new Option(item.name)); }
        } catch { }
    }, { once: true });
    const go = () => {
        const text = input.value.trim().toLocaleLowerCase('vi');
        if (!text) return;
        const hit = [...pages].find(([name]) => name.toLocaleLowerCase('vi').includes(text));
        if (hit) location.href = hit[1];
    };
    input.addEventListener('change', () => { if (pages.has(input.value.trim())) go(); });
    form.addEventListener('submit', e => { e.preventDefault(); go(); });
})();

// Link tới một mục thu gọn (<details id="…">) thì mở mục đó, ví dụ nút "Bộ học mới" → /StudySets#tao-bo-hoc.
(() => {
    const open = () => { const box = location.hash && document.getElementById(decodeURIComponent(location.hash.slice(1))); if (box instanceof HTMLDetailsElement) box.open = true; };
    open(); window.addEventListener('hashchange', open);
})();

// Lật thẻ 3D (hiệu ứng lật thẻ của nhóm: rotateY, tổng 0,5 giây). Thẻ của mình cao thấp theo nội dung nên không chồng hai mặt
// cố định như nhóm, mà quay nửa vòng → đổi mặt (swap) → quay nốt. Máy bật "giảm chuyển động" thì đổi mặt ngay, không quay.
window.MindSprintFlip = async (card, swap) => {
    if (card.dataset.flipping) return;
    if (!card.animate || matchMedia('(prefers-reduced-motion: reduce)').matches) { swap(); return; }
    card.dataset.flipping = '1';
    let swapped = false;
    try {
        await card.animate([{ transform: 'perspective(900px) rotateY(0deg)' }, { transform: 'perspective(900px) rotateY(90deg)' }],
            { duration: 250, easing: 'cubic-bezier(.4, 0, 1, 1)' }).finished;
        swap(); swapped = true;
        await card.animate([{ transform: 'perspective(900px) rotateY(-90deg)' }, { transform: 'perspective(900px) rotateY(0deg)' }],
            { duration: 250, easing: 'cubic-bezier(0, 0, .2, 1)' }).finished;
    } catch { if (!swapped) swap(); } // hiệu ứng bị hủy giữa chừng: vẫn đổi mặt đúng một lần
    finally { delete card.dataset.flipping; }
};

// Kiểm tra tệp ngay trên trình duyệt trước khi gửi (phân công nhóm: "bắt lỗi file dung lượng lớn trước khi gửi lên Server"):
// <input type="file" data-max-bytes="…" accept="…"> quá cỡ hoặc sai loại thì báo tại ô và bỏ tệp đã chọn. Máy chủ vẫn kiểm lại.
document.querySelectorAll('input[type="file"][data-max-bytes]').forEach(input => {
    const max = Number(input.dataset.maxBytes);
    const types = (input.accept || '').split(',').map(t => t.trim().toLowerCase()).filter(t => t.startsWith('.'));
    const mb = n => (n / 1024 / 1024).toLocaleString('vi', { maximumFractionDigits: 1 });
    input.addEventListener('change', () => {
        let message = '';
        for (const file of input.files) {
            const ext = file.name.includes('.') ? file.name.slice(file.name.lastIndexOf('.')).toLowerCase() : '';
            if (types.length && !types.includes(ext)) { message = `Tệp "${file.name}" không đúng loại. Chỉ nhận: ${types.join(', ')}.`; break; }
            if (file.size > max) { message = `Tệp "${file.name}" nặng ${mb(file.size)} MB, quá giới hạn ${mb(max)} MB. Hãy chọn tệp nhỏ hơn.`; break; }
        }
        input.setCustomValidity(message);
        if (message) { input.reportValidity(); input.value = ''; input.setCustomValidity(''); }
        let note = input.parentElement.querySelector('.file-check');
        if (!note) { note = document.createElement('p'); note.className = 'file-check text-danger small mt-1 mb-0'; note.setAttribute('role', 'alert'); input.after(note); }
        note.textContent = message;
    });
});

// Menu xổ trên thanh bên (Quản trị, menu tài khoản): mở sang phải thanh bằng position: fixed theo vị trí nút,
// để thanh bên vẫn cuộn được mà menu không bị vùng cuộn cắt. Thanh bên cuộn hoặc đổi cỡ cửa sổ thì đóng menu.
(() => {
    const desktop = matchMedia('(min-width: 992px)');
    const place = toggle => {
        const menu = toggle.parentElement.querySelector('.dropdown-menu');
        if (!menu || !desktop.matches || !toggle.closest('.has-sidebar .mindsprint-header')) return;
        const r = toggle.getBoundingClientRect();
        const left = r.right + 16;
        const top = Math.max(8, Math.min(toggle.closest('.account-menu') ? r.bottom - menu.offsetHeight : r.top, innerHeight - menu.offsetHeight - 8));
        Object.assign(menu.style, { left: `${left}px`, top: `${top}px`, right: 'auto', bottom: 'auto' });
        // Khối chứa có thể không phải cả màn hình (thanh bên có hiệu ứng): chỉnh theo độ lệch đo được.
        const m = menu.getBoundingClientRect();
        Object.assign(menu.style, { left: `${2 * left - m.left}px`, top: `${2 * top - m.top}px` });
    };
    document.addEventListener('shown.bs.dropdown', e => place(e.target));
    document.addEventListener('hidden.bs.dropdown', e => {
        const menu = e.target.parentElement?.querySelector('.dropdown-menu');
        if (menu) menu.style.left = menu.style.top = menu.style.right = menu.style.bottom = '';
    });
    const closeOpen = () => document.querySelectorAll('.has-sidebar .mindsprint-header [data-bs-toggle="dropdown"].show')
        .forEach(t => window.bootstrap?.Dropdown.getInstance(t)?.hide());
    document.querySelector('.has-sidebar .mindsprint-header .navbar')?.addEventListener('scroll', closeOpen, { passive: true });
    addEventListener('resize', closeOpen);
})();
