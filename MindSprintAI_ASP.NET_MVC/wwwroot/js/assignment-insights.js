(() => {
    const live = document.getElementById('assignment-live');
    const warnings = document.getElementById('pane-chong-gian-lan');
    const badge = document.getElementById('assignment-warning-count');
    if (live && warnings && badge) {
        const update = () => {
            // Keep a lecturer's pending approval/focus until they leave this panel.
            if (warnings.contains(document.activeElement)) return;
            const template = live.querySelector('[data-warning-template]');
            if (!template) return;
            warnings.replaceChildren(template.content.cloneNode(true));
            badge.textContent = warnings.querySelector('[data-warning-count]')?.dataset.warningCount || '0';
        };
        new MutationObserver(update).observe(live, { childList: true });
        warnings.addEventListener('focusout', () => setTimeout(update, 0));
    }
    const search = document.getElementById('matrix-search');
    if (!search) return;
    const normalized = text => text.normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/gi, 'd').toLocaleLowerCase('vi');
    function filter(q, attention) {
        document.querySelectorAll('[data-matrix-name]').forEach(row => {
            row.hidden = !normalized(row.dataset.matrixName).includes(normalized(q)) || attention && row.dataset.matrixAttention !== 'true';
        });
    }
    search.addEventListener('input', () => filter(search.value, false));
    document.addEventListener('mindsprint-gradebook-filter', event => {
        search.value = event.detail.q; filter(search.value, event.detail.onlyAttention);
    });
})();
