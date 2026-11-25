// (14) SignalR chỉ báo có thay đổi; dữ liệu luôn lấy lại qua endpoint kiểm chủ thông báo.
(() => {
    const badge = document.getElementById('notification-unread');
    if (!badge || typeof signalR === 'undefined') return;
    let stopped = false;
    async function refresh() {
        try {
            const response = await fetch('/Notifications/Count', { headers: { Accept: 'application/json' }, cache: 'no-store' });
            if (!response.ok) return;
            const data = await response.json();
            badge.textContent = String(data.unread);
            badge.setAttribute('aria-label', data.unread + ' thông báo chưa đọc');
            document.getElementById('notification-bell').setAttribute('aria-label', 'Thông báo, ' + data.unread + ' chưa đọc');
        } catch { /* Lần tải trang/kết nối lại vẫn đọc số lưu trong DB. */ }
    }
    const connection = new signalR.HubConnectionBuilder().withUrl('/hubs/learn').withAutomaticReconnect().build();
    connection.on('notification', refresh);
    connection.onreconnected(refresh);
    async function start() {
        if (stopped || connection.state !== signalR.HubConnectionState.Disconnected) return;
        try { await connection.start(); await refresh(); }
        catch { if (!stopped) setTimeout(start, 5000); }
    }
    connection.onclose(() => { if (!stopped) setTimeout(start, 5000); });
    window.addEventListener('pageshow', event => {
        if (event.persisted) { stopped = false; start(); refresh(); }
    });
    window.addEventListener('pagehide', () => { stopped = true; connection.stop(); });
    start();
})();
