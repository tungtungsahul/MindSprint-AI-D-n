// Trang làm bài theo đề tài liệu: đồng hồ, tự lưu bài viết, tải/xóa tệp đính kèm, lưu lần cuối trước khi nộp.
// Máy chủ vẫn là bên chốt: hết giờ, sai thiết bị, đã nộp thì mọi lần lưu bị từ chối và learn-live.js báo cho học viên.
(() => {
    const root = document.getElementById('play');
    if (!root || !root.classList.contains('paper-play')) return;
    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
    const text = document.getElementById('paper-text');
    const status = document.getElementById('paper-save-status');
    const list = document.getElementById('paper-files');
    const upload = document.getElementById('paper-upload');
    const uploadError = document.getElementById('paper-upload-error');
    const post = async (url, body) => {
        body.append('__RequestVerificationToken', token);
        const response = await fetch(url, { method: 'POST', body, headers: { Accept: 'application/json', 'X-Requested-With': 'fetch' } });
        const data = await response.json().catch(() => ({}));
        if (!response.ok) throw Object.assign(new Error(data.error || 'Chưa lưu được. Hãy kiểm tra mạng rồi thử lại.'), { rejected: !!data.error });
        return data;
    };

    // Tự lưu: 2 giây sau lần gõ cuối, khi rời ô viết và trước khi nộp. Chỉ gửi khi nội dung đổi.
    // Mỗi lúc chỉ một lần gửi; nội dung máy chủ đã từ chối (vd. quá dài) không gửi lại cho tới khi học viên sửa
    // (trên server 16/11 một lỗi lưu đã thành gần 300 lần gửi liên tục).
    let saved = text.value, timer = null, saving = null, rejected = null;
    const save = async () => {
        clearTimeout(timer);
        if (saving) { await saving.catch(() => {}); return save(); }
        if (text.value === saved) return true;
        const value = text.value;
        if (value === rejected) return false;
        status.textContent = 'Đang lưu…';
        saving = post(root.dataset.saveUrl, toForm({ text: value }));
        try {
            const data = await saving;
            saved = value;
            status.textContent = 'Đã lưu lúc ' + data.savedAt;
            return true;
        } catch (e) {
            if (e.rejected) rejected = value;
            status.textContent = e.message;
            return false;
        } finally { saving = null; }
    };
    function toForm(fields) { const f = new FormData(); Object.entries(fields).forEach(([k, v]) => f.append(k, v)); return f; }
    text.addEventListener('input', () => { status.textContent = 'Chưa lưu thay đổi mới'; clearTimeout(timer); timer = setTimeout(save, 2000); });
    text.addEventListener('blur', save);
    window.addEventListener('pagehide', () => {
        if (text.value === saved) return;
        // keepalive: vẫn gửi được khi trang đang đóng.
        const f = toForm({ text: text.value, __RequestVerificationToken: token });
        fetch(root.dataset.saveUrl, { method: 'POST', body: f, keepalive: true, headers: { 'X-Requested-With': 'fetch' } });
    });

    // Nộp: lưu bài viết trước (chạy trước hộp hỏi chung của site.js); lưu không được thì không nộp, tránh mất đoạn vừa viết.
    const finish = document.getElementById('finish-form');
    finish.addEventListener('submit', async event => {
        if (text.value === saved) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        if (await save()) finish.requestSubmit(event.submitter || undefined);
    }, true);

    // Tệp đính kèm.
    const fileItem = data => {
        const li = document.createElement('li');
        li.dataset.fileId = data.id;
        const a = document.createElement('a');
        a.href = data.url; a.target = '_blank'; a.rel = 'noopener'; a.textContent = data.name;
        const size = document.createElement('span');
        size.className = 'text-muted small'; size.textContent = ` (${Math.max(1, Math.ceil(data.size / 1024))} KB)`;
        const del = document.createElement('button');
        del.type = 'button'; del.className = 'btn btn-sm btn-link text-danger'; del.dataset.deleteFile = data.id;
        del.textContent = 'Xóa'; del.setAttribute('aria-label', 'Xóa tệp ' + data.name);
        li.append(a, ' ', size, ' ', del);
        return li;
    };
    upload.addEventListener('change', async () => {
        const file = upload.files[0];
        if (!file) return;
        uploadError.textContent = '';
        if (list.children.length >= Number(root.dataset.maxFiles)) { uploadError.textContent = `Mỗi bài tối đa ${root.dataset.maxFiles} tệp. Hãy xóa bớt tệp cũ.`; upload.value = ''; return; }
        upload.disabled = true;
        try { list.append(fileItem(await post(root.dataset.uploadUrl, toForm({ file })))); }
        catch (e) { uploadError.textContent = e.message; }
        finally { upload.disabled = false; upload.value = ''; }
    });
    list.addEventListener('click', async event => {
        const button = event.target.closest('[data-delete-file]');
        if (!button) return;   // không hỏi lại: bài còn mở thì tải lại tệp được ngay
        button.disabled = true;
        try {
            await post(root.dataset.deleteUrl, toForm({ fileId: button.dataset.deleteFile }));
            button.closest('li').remove();
        } catch (e) { uploadError.textContent = e.message; button.disabled = false; }
    });

    // Đồng hồ chỉ để hiển thị; giờ được cộng thêm đến qua learn-live.js (sự kiện learn:state).
    let deadline = root.dataset.deadline ? new Date(root.dataset.deadline) : null;
    const clock = document.getElementById('countdown');
    root.addEventListener('learn:state', event => {
        deadline = event.detail.deadlineAt ? new Date(event.detail.deadlineAt) : null;
        clock.classList.toggle('d-none', !deadline);
    });
    clock.classList.toggle('d-none', !deadline);
    const tick = async () => {
        if (!deadline || root.inert) return;
        const s = Math.floor((deadline - Date.now()) / 1000);
        if (s <= 0) { await save(); location.reload(); return; }   // máy chủ chốt lượt; tải lại để sang trang xem bài đã nộp
        const h = Math.floor(s / 3600), m = Math.floor(s % 3600 / 60), sec = s % 60;
        clock.textContent = 'Còn ' + [h, m, sec].map(n => String(n).padStart(2, '0')).join(':');
    };
    tick();
    setInterval(tick, 1000);
})();
