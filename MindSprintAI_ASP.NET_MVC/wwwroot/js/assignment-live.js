// (69, E) hub chỉ báo cần đọc lại; HTML luôn qua endpoint kiểm quyền giảng viên.
(() => {
    const root = document.getElementById('assignment-live');
    if (!root) return;
    const status = document.getElementById('assignment-live-status');
    const summary = document.getElementById('assignment-live-summary');
    let stopped = false, debounce, retry, fetching = false, again = false, pending = null;
    const rechecks = new Set();
    const counts = () => JSON.stringify(root.querySelector('[data-live-counts]')?.dataset);
    let previousCounts = counts();
    // Reuse each table's own headings as labels on narrow screens, including refreshed rows.
    function labelRows() {
        root.querySelectorAll('table').forEach(table => {
            const headings = [...table.querySelectorAll('thead th')].map(th => th.textContent.trim());
            table.querySelectorAll('tbody tr').forEach(row => [...row.cells].forEach((cell, index) => {
                cell.dataset.label = headings[index] || 'Thao tác';
            }));
            table.classList.add('live-table');
        });
    }
    labelRows();
    // Người dùng báo 14/11/2026: bảng tự cập nhật thì nhảy về đầu. Bảng điểm nằm trong khung cuộn riêng (.table-responsive);
    // thay bảng mới thì khung mới bắt đầu ở đầu. Giữ học viên đang nhìn ở đúng chỗ cũ trên màn hình (trong khung, hoặc cả trang trên điện thoại).
    const page = () => document.scrollingElement || document.documentElement;
    function scrollerOf(row) {
        const box = row.closest('.table-responsive');
        return box && /(auto|scroll)/.test(getComputedStyle(box).overflowY) && box.scrollHeight > box.clientHeight ? box : page();
    }
    function anchor() {
        for (const row of root.querySelectorAll('tr[data-row]')) {
            if (row.hidden || !row.offsetParent) continue;
            const box = scrollerOf(row), edge = box === page() ? 0 : Math.max(0, box.getBoundingClientRect().top);
            if (row.getBoundingClientRect().bottom > edge) return { key: row.dataset.row, table: row.closest('table')?.className, top: row.getBoundingClientRect().top };
        }
        return null;
    }
    function swap() {
        // (E) giữ nguyên phần tử đang dùng bằng bàn phím, cập nhật khi focus rời bảng.
        if (!pending || stopped || root.contains(document.activeElement)) return;
        const before = anchor(), y = page().scrollTop;
        const boxes = [...root.querySelectorAll('.table-responsive')].map(n => [n.scrollLeft, n.scrollTop]); // cả cuộn ngang bảng rộng
        root.replaceChildren(pending);
        labelRows();
        pending = null;
        // Bộ chia trang và nút "Xem chi tiết" dựng lại trong hàng đợi vi tác vụ ngay sau khi bảng đổi; đặt việc đo sau chúng.
        // Không dùng requestAnimationFrame: trình duyệt không chạy nó khi trang bị ẩn/thu nhỏ.
        // behavior 'instant': trang đặt scroll-behavior: smooth, gán scrollTop sẽ trượt từ từ và đọc lại giữa chừng ra số sai.
        queueMicrotask(() => {
            page().scrollTo({ top: y, behavior: 'instant' });
            root.querySelectorAll('.table-responsive').forEach((n, i) => { if (boxes[i]) n.scrollTo({ left: boxes[i][0], top: boxes[i][1], behavior: 'instant' }); });
            const same = before && [...root.querySelectorAll('tr[data-row]')]
                .find(r => r.dataset.row === before.key && r.closest('table')?.className === before.table && !r.hidden && r.offsetParent);
            if (same) { const s = scrollerOf(same); s.scrollTo({ top: s.scrollTop + same.getBoundingClientRect().top - before.top, behavior: 'instant' }); }
        });
        const current = counts();
        if (current !== previousCounts) {
            const c = root.querySelector('[data-live-counts]').dataset;
            summary.textContent = `${c.participants} học viên, ${c.waiting} đang chờ, ${c.working} đang làm, ${c.paused} tạm dừng, ${c.submitted} chờ công bố, ${c.completed} hoàn thành.`;
            previousCounts = current;
        }
    }
    root.addEventListener('focusout', () => setTimeout(swap, 0));
    let lastStart = 0, dirty = false;
    async function refresh() {
        if (stopped) return;
        // Tab đang ẩn thì không tải (GV không nhìn); quay lại tab thì tải ngay một lần.
        if (document.hidden) { dirty = true; return; }
        if (fetching) { again = true; return; }
        fetching = true; lastStart = Date.now();
        try {
            const response = await fetch(root.dataset.liveUrl, { cache: 'no-store', headers: { Accept: 'text/html' } });
            if (!response.ok || response.redirected) throw new Error();
            const template = document.createElement('template');
            template.innerHTML = await response.text();
            if (!template.content.querySelector('[data-live-counts]')) throw new Error();
            if (connection && connection.state === signalR.HubConnectionState.Connected) status.textContent = 'Đang cập nhật trực tiếp';
            pending = template.content;
            swap();
        } catch { status.textContent = 'Mất kết nối, trang tự thử lại'; }
        finally {
            fetching = false;
            if (again && !stopped) { again = false; schedule(); }
        }
    }
    function schedule() {
        // (H3) nhiều thông báo liên tục không được dời lần cập nhật mãi mãi.
        if (debounce) return;
        // Thử tải 14/11/2026: 650 học viên thao tác liên tục → trang tự làm mới không nghỉ, mỗi lần dựng lại cả bảng. Tối đa 5 giây/lần.
        debounce = setTimeout(() => { debounce = null; refresh(); }, Math.max(1000, 5000 - (Date.now() - lastStart)));
    }
    document.addEventListener('visibilitychange', () => { if (!document.hidden && dirty) { dirty = false; schedule(); } });
    const poll = setInterval(refresh, 60000); // (E) dự phòng cả khi thư viện/hub không kết nối được.
    let connection;
    if (typeof signalR !== 'undefined') {
        connection = new signalR.HubConnectionBuilder().withUrl(root.dataset.hubUrl)
            .withAutomaticReconnect([0, 2000, 10000, 30000]).build();
        connection.on('lecturers-changed', event => {
            if (event.assignmentId !== Number(root.dataset.assignment)) return;
            schedule();
            if (Number.isInteger(event.recheckAfterSeconds) && event.recheckAfterSeconds > 0) {
                // Qua giới hạn 5 giây: nhiều học viên rớt mạng cùng lúc không làm trang tải liên tục (tài khoản 2 rà 14/11/2026).
                const timer = setTimeout(() => { rechecks.delete(timer); schedule(); }, event.recheckAfterSeconds * 1000);
                rechecks.add(timer);
            }
        });
        async function join() {
            try {
                await connection.invoke('JoinLecturer', Number(root.dataset.assignment));
                status.textContent = 'Đang cập nhật trực tiếp';
                await refresh(); // (E) đọc bù thay đổi xảy ra trước khi vào nhóm/kết nối lại.
            } catch { status.textContent = 'Mất kết nối, trang tự thử lại'; }
        }
        async function start() {
            if (stopped) return;
            try { await connection.start(); await join(); }
            catch {
                status.textContent = 'Mất kết nối, trang tự thử lại';
                retry = setTimeout(start, 5000);
            }
        }
        connection.onreconnecting(() => { status.textContent = 'Mất kết nối, trang tự thử lại'; });
        connection.onreconnected(join);
        connection.onclose(() => {
            status.textContent = 'Mất kết nối, trang tự thử lại';
            if (!stopped) retry = setTimeout(start, 5000);
        });
        start();
    } else status.textContent = 'Mất kết nối, trang tự thử lại';
    window.addEventListener('pagehide', () => {
        stopped = true;
        clearInterval(poll); clearTimeout(debounce); clearTimeout(retry);
        for (const timer of rechecks) clearTimeout(timer);
        connection?.stop();
    });
})();
