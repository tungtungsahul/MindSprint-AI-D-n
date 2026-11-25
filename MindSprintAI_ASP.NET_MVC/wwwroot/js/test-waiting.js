(() => {
    const root = document.getElementById('test-waiting');
    if (!root) return;
    let timer, entering = false, stopped = false;
    async function poll() {
        try {
            const response = await fetch(root.dataset.statusUrl, { headers: { Accept: 'application/json' }, cache: 'no-store' });
            const status = await response.json();
            if (!response.ok) throw new Error(status.error || 'Không kiểm tra được trạng thái bài.');
            if (!status.reason && status.approved && !status.canStart && !status.started
                && document.querySelector('h1')?.textContent === 'Chờ giảng viên duyệt') {
                location.reload();
                return;
            }
            if (status.canStart && !entering) {
                entering = true;
                clearInterval(timer);
                document.getElementById('waiting-button').classList.remove('d-none');
                document.getElementById('waiting-start').requestSubmit();
            } else {
                document.getElementById('waiting-status').textContent = status.reason || (status.started
                    ? 'Phiên đã kết thúc hoặc đã đóng. Hãy quay về trang bài này.' : 'Chờ giảng viên bắt đầu. Trang tự cập nhật mỗi 30 giây.');
            }
        } catch (error) { document.getElementById('waiting-status').textContent = error.message; }
    }
    timer = setInterval(poll, 30000); // (B2) dự phòng khi mất thông báo SignalR.
    poll();
    if (typeof signalR === 'undefined') return;
    const connection = new signalR.HubConnectionBuilder().withUrl(root.dataset.hubUrl)
        .withAutomaticReconnect([0, 2000, 10000, 30000]).build();
    // Thử tải 13/11/2026: GV bấm Bắt đầu thì cả phòng nhận tin cùng lúc; mỗi máy chờ ngẫu nhiên 0–5 giây để không cùng ập vào.
    connection.on('test-started', () => {
        document.getElementById('waiting-status').textContent = 'Giảng viên đã bắt đầu. Đang vào bài…';
        setTimeout(poll, Math.random() * 5000);
    });
    connection.on('stranger-changed', notice => {
        if (notice.assignmentId === Number(root.dataset.assignment)) location.reload();
    });
    async function join() {
        try {
            await connection.invoke('JoinWaiting', Number(root.dataset.assignment));
            await poll(); // Bắt đầu có thể xảy ra trước khi kịp vào nhóm.
        } catch { document.getElementById('waiting-status').textContent = 'Chưa kết nối được thông báo. Trang tự cập nhật mỗi 30 giây.'; }
    }
    async function start() {
        if (stopped || entering) return;
        try { await connection.start(); await join(); }
        catch { setTimeout(start, 5000); }
    }
    connection.onreconnected(join);
    connection.onclose(() => { if (!stopped) setTimeout(start, 5000); });
    window.addEventListener('pagehide', () => { stopped = true; connection.stop(); });
    start();
})();
