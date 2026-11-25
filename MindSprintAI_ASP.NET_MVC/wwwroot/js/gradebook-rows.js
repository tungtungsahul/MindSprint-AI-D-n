// Bảng điểm trên điện thoại: mỗi học viên chỉ hiện tên, trạng thái, điểm; bấm "Xem chi tiết" để mở phần còn lại.
// Không có JavaScript thì bảng hiện đủ như cũ. Dòng đang mở được giữ lại khi bảng tự cập nhật trực tiếp.
(() => {
    'use strict';
    const root = document.getElementById('assignment-live');
    if (!root) return;
    const open = new Set();
    root.classList.add('is-collapsible');
    function sync() {
        root.querySelectorAll('tr[data-row]').forEach(row => {
            const button = row.querySelector('.b-row-toggle');
            if (!button) return;
            const isOpen = open.has(row.dataset.row);
            row.classList.toggle('is-open', isOpen);
            button.hidden = false;
            button.setAttribute('aria-expanded', String(isOpen));
            const label = isOpen ? 'Thu gọn' : 'Xem chi tiết';
            // Chỉ ghi khi khác: đổi chữ tạo thay đổi cây DOM, sẽ gọi lại sync qua MutationObserver.
            if (button.textContent !== label) button.textContent = label;
        });
    }
    root.addEventListener('click', event => {
        const button = event.target.closest('.b-row-toggle');
        const row = button?.closest('tr[data-row]');
        if (!row) return;
        if (open.has(row.dataset.row)) open.delete(row.dataset.row); else open.add(row.dataset.row);
        sync();
    });
    if (typeof MutationObserver !== 'undefined') new MutationObserver(sync).observe(root, { childList: true, subtree: true });
    sync();
})();
