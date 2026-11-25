(() => {
    'use strict';
    const root = document.getElementById('assignment-live');
    const tools = document.getElementById('gradebook-tools');
    const pages = document.getElementById('gradebook-pages');
    if (!root || !tools || !pages || typeof MutationObserver === 'undefined') return;
    const search = document.getElementById('gradebook-search');
    const size = document.getElementById('gradebook-size');
    const attention = document.getElementById('gradebook-attention');
    const groups = [...tools.querySelectorAll('[data-grade-group]')];
    const normalize = value => value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D').toLocaleLowerCase('vi');
    const flagged = row => row.classList.contains('table-warning') || !!row.querySelector('.device-change-note') || /Rời màn hình\s+[1-9]\d*\s+lần|Tự nộp.*rời màn hình/i.test(row.textContent);
    const originalRows = new WeakMap();
    let group = 0, page = 1, onlyAttention = false;
    try {
        const saved = localStorage.getItem('mindsprint-gradebook-size');
        if (['10', '25', '50', 'all'].includes(saved)) size.value = saved;
    } catch { /* Vẫn dùng được khi máy không lưu lựa chọn. */ }
    function button(label, target, disabled, current = false) {
        const node = document.createElement('button');
        node.type = 'button'; node.className = 'btn'; node.textContent = label;
        node.disabled = disabled;
        if (current) node.setAttribute('aria-current', 'page');
        node.addEventListener('click', () => { page = target; render(); });
        return node;
    }
    function render() {
        observer.disconnect();
        try {
            const tables = [...root.querySelectorAll('.b-grade-table')];
            if (tables.length !== 2) return;
            const rows = tables.map(table => {
                const body = table.tBodies[0];
                if (!originalRows.has(body)) originalRows.set(body, [...body.rows]);
                return originalRows.get(body);
            });
            const all = rows[group];
            const ordered = [...all.filter(flagged), ...all.filter(row => !flagged(row))];
            // Stable partition: preserve original order inside both groups, including after refresh.
            ordered.forEach(row => tables[group].tBodies[0].append(row));
            const matched = ordered.filter(row => normalize(row.cells[0].firstChild?.textContent || row.cells[0].textContent).includes(normalize(search.value)) && (!onlyAttention || flagged(row)));
            const perPage = size.value === 'all' ? Math.max(1, matched.length) : Number(size.value);
            const last = Math.max(1, Math.ceil(matched.length / perPage));
            page = Math.min(page, last);
            const shown = new Set(matched.slice((page - 1) * perPage, page * perPage));
            rows.forEach((list, index) => {
                tables[index].parentElement.hidden = index !== group;
                const empty = tables[index].parentElement.nextElementSibling;
                if (empty?.classList.contains('b-table-empty')) empty.hidden = index !== group;
                list.forEach(row => { row.hidden = index === group && !shown.has(row); });
                groups[index].textContent = `${index === 0 ? 'Trong lớp' : 'Người ngoài lớp'} (${list.length})`;
                groups[index].setAttribute('aria-pressed', String(group === index));
            });
            root.querySelectorAll('.b-stranger-title, .b-stranger-title + p').forEach(node => { node.hidden = group !== 1; });
            attention.textContent = `Cần chú ý (${all.filter(flagged).length})`;
            attention.setAttribute('aria-pressed', String(onlyAttention));
            const nav = document.createElement('nav'); nav.setAttribute('aria-label', 'Trang bảng điểm');
            nav.append(button('Trước', page - 1, page === 1));
            const numbers = new Set([1, last, page - 1, page, page + 1].filter(n => n >= 1 && n <= last));
            let previous = 0;
            [...numbers].sort((a, b) => a - b).forEach(n => {
                if (previous && n > previous + 1) nav.append(document.createTextNode(' … '));
                nav.append(button(String(n), n, n === page, n === page)); previous = n;
            });
            nav.append(button('Sau', page + 1, page === last));
            const summary = document.createElement('p'); summary.setAttribute('aria-live', 'polite');
            summary.textContent = `Đang hiện ${matched.length ? (page - 1) * perPage + 1 : 0}–${Math.min(page * perPage, matched.length)} trên ${matched.length} học viên`;
            pages.replaceChildren(nav, summary);
            tools.hidden = false; pages.hidden = false;
            document.dispatchEvent(new CustomEvent('mindsprint-gradebook-filter', { detail: { q: search.value, onlyAttention } }));
        } catch {
            root.querySelectorAll('[hidden]').forEach(node => { node.hidden = false; });
            tools.hidden = true; pages.hidden = true;
        } finally { observer.observe(root, { childList: true, subtree: true }); }
    }
    const observer = new MutationObserver(render);
    search.addEventListener('input', () => { page = 1; render(); });
    size.addEventListener('change', () => {
        try { localStorage.setItem('mindsprint-gradebook-size', size.value); } catch { /* Chỉ nhớ trên trang này. */ }
        page = 1; render();
    });
    attention.addEventListener('click', () => { onlyAttention = !onlyAttention; page = 1; render(); });
    groups.forEach((node, index) => node.addEventListener('click', () => { group = index; page = 1; render(); }));
    render();
})();
