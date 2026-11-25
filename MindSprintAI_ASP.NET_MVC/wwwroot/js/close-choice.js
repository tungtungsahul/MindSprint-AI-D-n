// Đóng bài giao khi còn học viên đang làm: máy chủ trả trang "Đóng ngay / Đóng sau 15 phút".
// Ở đây hiện luôn thành hộp nổi trên trang bảng điểm (nền tối mờ) thay vì chuyển sang trang khác.
// Không có JavaScript thì form vẫn gửi bình thường và hiện trang chọn như cũ.
(() => {
    const form = document.querySelector('form[data-close-popup]');
    if (!form || !window.bootstrap?.Modal || !window.fetch) return;
    form.addEventListener('submit', async e => {
        if (!form.dataset.confirmed) return; // lần gửi đầu để site.js hỏi xác nhận trước
        e.preventDefault();
        try {
            // redirect: 'manual' để lời nhắn "Đã đóng bài giao" còn nguyên cho lần tải trang kế tiếp.
            const res = await fetch(form.action, { method: 'POST', body: new FormData(form), credentials: 'same-origin', redirect: 'manual' });
            const card = res.type === 'opaqueredirect' ? null
                : new DOMParser().parseFromString(await res.text(), 'text/html').querySelector('[role="alertdialog"]');
            if (!card) { location.reload(); return; }
            const el = document.createElement('div');
            el.className = 'modal fade'; el.tabIndex = -1; el.setAttribute('aria-labelledby', 'close-title');
            el.innerHTML = '<div class="modal-dialog modal-dialog-centered"><div class="modal-content"><div class="modal-body"></div></div></div>';
            el.querySelector('.modal-body').append(...document.importNode(card, true).childNodes);
            el.querySelectorAll('a.btn-outline-secondary').forEach(a => { a.setAttribute('data-bs-dismiss', 'modal'); a.setAttribute('role', 'button'); });
            document.body.append(el);
            el.addEventListener('hidden.bs.modal', () => el.remove());
            el.addEventListener('shown.bs.modal', () => (el.querySelector('[autofocus]') || el).focus(), { once: true });
            new bootstrap.Modal(el).show();
        } catch { form.submit(); }
    });
})();
