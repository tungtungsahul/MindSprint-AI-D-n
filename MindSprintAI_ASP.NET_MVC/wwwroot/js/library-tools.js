(() => {
    const normalize = value => value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/gi, 'd').toLocaleLowerCase('vi');
    const list = document.querySelector('.c-library .set-grid');
    const search = document.getElementById('library-search');
    const sort = document.getElementById('library-sort');
    if (list && search && sort) {
        document.querySelector('.c-library-tools').hidden = false;
        const category = document.getElementById('library-category');
        const empty = document.querySelector('.c-library-empty');
        const filter = () => {
            [...list.children].forEach(row => {
                row.hidden = !normalize(row.dataset.libraryName).includes(normalize(search.value))
                    || (category?.value ? row.dataset.libraryCategory !== category.value : false);
            });
            if (empty) empty.hidden = [...list.children].some(row => !row.hidden);
        };
        search.addEventListener('input', filter);
        category?.addEventListener('change', filter);
        sort.addEventListener('change', () => {
            [...list.children].sort((a, b) => sort.value === 'name'
                ? a.dataset.libraryName.localeCompare(b.dataset.libraryName, 'vi')
                : Number(b.dataset.libraryCreated) - Number(a.dataset.libraryCreated)).forEach(row => list.append(row));
        });
    }
    // Open the existing creation/details form when reached via an in-page link.
    const openTarget = () => {
        const target = document.getElementById(location.hash.slice(1));
        if (!target) return;
        let node = target.parentElement;
        while (node) { if (node.tagName === 'DETAILS') node.open = true; node = node.parentElement; }
        target.scrollIntoView({ block: 'center' });
        // "Sửa bộ học" dẫn tới nút Khác của bộ đó: mở luôn menu và đặt con trỏ vào ô tên.
        if (target.dataset.bsToggle === 'dropdown' && window.bootstrap) {
            const input = target.parentElement.querySelector('.set-rename-input');
            if (input) target.addEventListener('shown.bs.dropdown', () => setTimeout(() => { input.focus(); input.select(); }), { once: true });
            bootstrap.Dropdown.getOrCreateInstance(target).show();
            if (input) return;
        }
        target.focus();
    };
    window.addEventListener('hashchange', openTarget);
    if (location.hash) openTarget();
    const toggle = document.querySelector('[data-answer-toggle]');
    if (!toggle) return;
    try { toggle.checked = localStorage.getItem('mindsprint-show-answers') !== 'false'; } catch { }
    const apply = () => document.querySelector('.c-study-details')?.classList.toggle('c-answers-hidden', !toggle.checked);
    toggle.addEventListener('change', () => {
        try { localStorage.setItem('mindsprint-show-answers', String(toggle.checked)); } catch { }
        apply();
    });
    apply();
})();
