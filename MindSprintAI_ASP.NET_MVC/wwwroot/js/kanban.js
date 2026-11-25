// Trang Công việc (Kanban): mở hộp thêm/sửa, lọc theo chữ + ưu tiên, kéo thả giữa các cột.
// Thêm/sửa/xóa/nút mũi tên là form thường nên vẫn chạy khi JS lỗi; JS chỉ thêm tiện ích.
(() => {
    const modal = document.getElementById('task-modal');
    const form = document.getElementById('task-form');
    if (!modal || !form) return;
    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
    const status = document.getElementById('kanban-status');

    // Cùng một hộp cho "thêm" (nút có data-status) và "sửa" (nút có data-id kèm dữ liệu việc).
    modal.addEventListener('show.bs.modal', e => {
        const d = e.relatedTarget?.dataset ?? {};
        form.reset();
        form.action = d.id ? `${form.dataset.edit}/${d.id}` : form.dataset.create;
        document.getElementById('task-modal-title').textContent = d.id ? 'Sửa công việc' : 'Tạo công việc mới';
        form.elements.Title.value = d.title ?? '';
        form.elements.Description.value = d.description ?? '';
        form.elements.Priority.value = d.priority ?? 'Medium';
        form.elements.Status.value = d.status ?? 'Todo';
        form.elements.DueDate.value = d.due ?? '';
    });
    modal.addEventListener('shown.bs.modal', () => form.elements.Title.focus());

    const search = document.getElementById('kanban-search');
    const priority = document.getElementById('kanban-priority');
    const filter = () => {
        const q = search.value.trim().toLowerCase();
        document.querySelectorAll('.kanban-card').forEach(card => {
            card.hidden = (q && !card.dataset.search.includes(q)) || (priority.value && card.dataset.priority !== priority.value);
        });
    };
    search.addEventListener('input', filter);
    priority.addEventListener('change', filter);

    // Kéo thả: thả lên một thẻ thì chèn trước thẻ đó, thả vào chỗ trống thì về cuối cột.
    let dragged = null;
    document.querySelectorAll('.kanban-card').forEach(card => {
        card.addEventListener('dragstart', e => { dragged = card; card.classList.add('is-dragging'); e.dataTransfer.effectAllowed = 'move'; });
        card.addEventListener('dragend', () => { card.classList.remove('is-dragging'); clear(); dragged = null; });
    });
    const clear = () => document.querySelectorAll('.drop-before, .drop-over').forEach(el => el.classList.remove('drop-before', 'drop-over'));
    const before = (list, y) => [...list.querySelectorAll('.kanban-card:not(.is-dragging):not([hidden])')]
        .find(c => { const r = c.getBoundingClientRect(); return y < r.top + r.height / 2; });

    document.querySelectorAll('.kanban-list').forEach(list => {
        list.addEventListener('dragover', e => {
            if (!dragged) return;
            e.preventDefault();
            clear();
            list.classList.add('drop-over');
            before(list, e.clientY)?.classList.add('drop-before');
        });
        list.addEventListener('dragleave', e => { if (!list.contains(e.relatedTarget)) clear(); });
        list.addEventListener('drop', async e => {
            e.preventDefault();
            if (!dragged) return;
            const target = before(list, e.clientY);
            // Vị trí tính trong cột đích khi đã bỏ thẻ đang kéo ra (như KanbanService.ArrangeAsync).
            const cards = [...list.querySelectorAll('.kanban-card')].filter(c => c !== dragged);
            const position = target ? cards.indexOf(target) : cards.length;
            clear();
            const body = new URLSearchParams({ status: list.dataset.status, position });
            const res = await fetch(`${document.querySelector(".kanban-board").dataset.move}/${dragged.dataset.id}`, {
                method: 'POST', body, headers: { 'Accept': 'application/json', 'RequestVerificationToken': token }
            }).catch(() => null);
            if (res?.ok) { location.reload(); return; } // shortcut: tải lại để số đếm/nút mũi tên khớp DB; đổi sang vẽ lại tại chỗ nếu bảng dài
            const err = await res?.json().catch(() => null);
            status.textContent = err?.error ?? 'Không chuyển được công việc, bạn thử lại nhé.';
            status.dataset.type = 'error';
        });
    });
})();
