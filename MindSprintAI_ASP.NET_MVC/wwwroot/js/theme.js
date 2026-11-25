(() => {
    const root = document.documentElement;
    const controls = [...document.querySelectorAll('[data-theme-toggle]')];
    const system = window.matchMedia('(prefers-color-scheme: dark)');
    function savedTheme() {
        try { return localStorage.getItem('mindsprint-theme'); } catch { return null; }
    }
    function apply(theme) {
        root.setAttribute('data-bs-theme', theme);
        // Both scheme metas follow a manual choice, including when it differs from the device.
        document.querySelectorAll('meta[name="theme-color"]').forEach(meta => {
            meta.setAttribute('content', theme === 'dark' ? '#131317' : '#F3F7DC');
        });
        controls.forEach(button => {
            const dark = theme === 'dark';
            // Nhãn cố định, bật/tắt do aria-pressed báo: đổi cả nhãn lẫn trạng thái làm trình đọc màn hình đọc sai.
            button.setAttribute('aria-pressed', String(dark));
        });
    }
    apply(root.getAttribute('data-bs-theme') || (system.matches ? 'dark' : 'light'));
    controls.forEach(button => button.addEventListener('click', () => {
        const theme = root.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark';
        try { localStorage.setItem('mindsprint-theme', theme); } catch { /* This page can still change. */ }
        apply(theme);
    }));
    system.addEventListener('change', () => {
        const saved = savedTheme();
        if (saved !== 'light' && saved !== 'dark') apply(system.matches ? 'dark' : 'light');
    });
    window.addEventListener('storage', event => {
        if (event.key === 'mindsprint-theme') apply(event.newValue === 'light' || event.newValue === 'dark'
            ? event.newValue : system.matches ? 'dark' : 'light');
        if (event.key === 'mindsprint-sidebar') setRail(event.newValue === 'collapsed');
    });

    // Thanh bên trái (≥ 992px): thu gọn chỉ còn icon hoặc mở rộng có chữ; nhớ lựa chọn trên máy này.
    const rail = document.querySelector('[data-sidebar-toggle]');
    function setRail(collapsed) {
        if (collapsed) root.setAttribute('data-sidebar', 'collapsed'); else root.removeAttribute('data-sidebar');
        if (!rail) return;
        rail.setAttribute('aria-expanded', String(!collapsed));
        rail.setAttribute('aria-label', collapsed ? 'Mở rộng thanh bên' : 'Thu gọn thanh bên');
    }
    setRail(root.getAttribute('data-sidebar') === 'collapsed');
    rail?.addEventListener('click', () => {
        const collapsed = root.getAttribute('data-sidebar') !== 'collapsed';
        try { localStorage.setItem('mindsprint-sidebar', collapsed ? 'collapsed' : 'open'); } catch { /* Vẫn đổi được trên trang này. */ }
        setRail(collapsed);
    });
})();
