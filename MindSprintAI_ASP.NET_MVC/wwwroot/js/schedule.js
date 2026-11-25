// Trang Lịch học: một hộp cho "thêm" (nút Thêm lịch học) và "sửa" (bấm vào giờ học). Lưu/xóa là form thường.
(() => {
    const modal = document.getElementById('slot-modal');
    const form = document.getElementById('slot-form');
    if (!modal || !form) return;
    modal.addEventListener('show.bs.modal', e => {
        const d = e.relatedTarget?.dataset ?? {};
        form.reset();
        form.action = d.id ? `${form.dataset.edit}/${d.id}` : form.dataset.create;
        document.getElementById('slot-modal-title').textContent = d.id ? 'Sửa giờ học' : 'Thêm giờ học mới';
        form.elements.DayOfWeek.value = d.day ?? '1';
        form.elements.StartTime.value = d.start ?? '';
        form.elements.EndTime.value = d.end ?? '';
        form.elements.Topic.value = d.topic ?? 'Mixed';
        form.elements.Note.value = d.note ?? '';
    });
    // Như nhóm: báo ngay khi giờ kết thúc không sau giờ bắt đầu (máy chủ vẫn kiểm lại).
    form.addEventListener('input', () => {
        const end = form.elements.EndTime;
        end.setCustomValidity(form.elements.StartTime.value && end.value && end.value <= form.elements.StartTime.value
            ? 'Giờ kết thúc phải muộn hơn giờ bắt đầu học.' : '');
    });
})();
