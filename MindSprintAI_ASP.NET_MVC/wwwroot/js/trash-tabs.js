(() => {
    const tabs = document.querySelector('.c-trash-tabs');
    if (!tabs || tabs.dataset.empty) return; // Thùng rác trống: không hiện hàng lọc.
    tabs.hidden = false;
    tabs.querySelectorAll('[data-trash-filter]').forEach(button => button.addEventListener('click', () => {
        tabs.querySelectorAll('button').forEach(node => node.setAttribute('aria-pressed', String(node === button)));
        document.querySelectorAll('[data-trash-kind]').forEach(row => {
            row.hidden = button.dataset.trashFilter !== 'all' && row.dataset.trashKind !== button.dataset.trashFilter;
        });
    }));
})();
