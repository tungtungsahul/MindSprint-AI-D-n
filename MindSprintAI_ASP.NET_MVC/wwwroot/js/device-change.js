// (F4) trang máy xin chuyển chỉ đọc quyết định của lượt thuộc chính người đang dùng.
(() => {
    const root = document.getElementById('device-change');
    if (!root) return;
    let stopped = false, timer;
    async function poll() {
        try {
            const response = await fetch(root.dataset.stateUrl, { cache: 'no-store', headers: { Accept: 'application/json' } });
            if (response.ok && !response.redirected) {
                const state = await response.json();
                if (state.ended) { location.assign(root.dataset.reviewUrl); return; }
                if (state.active) { location.assign(root.dataset.playUrl); return; }
                if (state.message) document.getElementById('device-change-message').textContent = state.message;
                const cancel = root.querySelector('form[action*="CancelDeviceChange"]');
                if (cancel && state.status !== 'Pending') { location.reload(); return; }
            }
        } catch { /* hỏi lại khi có mạng */ }
        if (!stopped) timer = setTimeout(poll, 2000);
    }
    poll();
    window.addEventListener('pagehide', () => { stopped = true; clearTimeout(timer); });
})();
