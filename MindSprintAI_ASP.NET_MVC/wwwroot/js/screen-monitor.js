// (H2) chỉ được nạp ở Play sau đồng ý; máy chủ tự tính giờ và số lần.
(() => {
    const root = document.getElementById('play');
    const url = root?.dataset.screenUrl;
    if (!url) return;
    const verification = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    let queue = Promise.resolve(), away = false, exiting = false, stopped = false, flushFails = 0;
    // (H10) lần rời lúc mất mạng: nhớ qua cả tải lại trang, báo lên trước mọi tín hiệu khác khi có mạng.
    const storeKey = 'qem_offline_' + url;
    let pending = [];
    try { pending = JSON.parse(sessionStorage.getItem(storeKey) || '[]'); } catch { pending = []; }
    function savePending() { try { sessionStorage.setItem(storeKey, JSON.stringify(pending)); } catch { /* chỉ mất khả năng nhớ qua tải lại */ } }
    // (H9) che đề khi mất mạng; máy chủ báo lỗi thì không che vì lỗi không do học viên.
    const cover = document.createElement('p');
    cover.className = 'alert alert-warning offline-cover'; cover.setAttribute('role', 'alert'); cover.hidden = true;
    cover.textContent = 'Mất kết nối Internet. Đề tạm ẩn cho đến khi có mạng lại. Thời gian làm bài vẫn tính.';
    root.prepend(cover);
    function setOffline(value) { cover.hidden = !value; root.classList.toggle('is-offline', value); }
    function body(eventType, kind) {
        const data = new FormData(); data.set('__RequestVerificationToken', verification || '');
        data.set('eventType', eventType); data.set('clientAt', new Date().toISOString());
        if (kind) data.set('kind', String(kind));
        return data;
    }
    function offlineBody() {
        const data = body('offline'), now = Date.now();
        for (const p of pending) {
            data.append('offlineKind', String(p.kind));
            data.append('offlineAgo', String(Math.max(0, Math.round((now - p.at) / 1000))));
            data.append('offlineSeconds', String(p.back ? Math.max(0, Math.round((p.back - p.at) / 1000)) : -1));
        }
        return data;
    }
    // 'network' = không tới được máy chủ; 'error' = máy chủ trả lỗi; 'ok' = đã nhận.
    async function post(data) {
        let response;
        try {
            response = await fetch(url, { method: 'POST', body: data, keepalive: true, headers: { Accept: 'application/json' } });
        } catch { setOffline(true); return 'network'; }
        setOffline(false);
        if (!response.ok || response.redirected) {
            // Trang đã cũ: play.js hiện nút "Tải lại trang" (học viên ngồi yên vẫn thấy, không chỉ khi bấm).
            if (response.status === 400) response.json().then(r => { if (r.reload) document.dispatchEvent(new CustomEvent('learn:stale', { detail: r.error })); }).catch(() => {});
            return 'error';
        }
        try {
            const reply = await response.json();
            if (reply.banner) {
                const banner = document.getElementById('screen-return');
                banner.textContent = reply.banner; banner.classList.remove('d-none');
            }
            if (reply.ended) { stopped = true; location.assign(reply.reviewUrl); }
        } catch { /* trả lời hỏng: coi như đã nhận */ }
        return 'ok';
    }
    function remember(eventType, kind) {
        if (eventType === 'start' && (kind === 1 || kind === 2) && pending.length < 20) {
            pending.push({ kind, at: Date.now(), back: null }); savePending();
        } else if (eventType === 'end' && pending.length && !pending[pending.length - 1].back) {
            pending[pending.length - 1].back = Date.now(); savePending();
        }
    }
    function send(eventType, kind) {
        queue = queue.then(async () => {
            if (stopped || root.inert) return;
            if (pending.length) {
                const flushed = await post(offlineBody());
                if (flushed === 'network') { remember(eventType, kind); return; }
                // lỗi máy chủ: giữ lại thử ở tín hiệu sau; 3 lần liền thì bỏ để không chặn tín hiệu thường mãi.
                if (flushed === 'ok' || ++flushFails >= 3) { pending = []; flushFails = 0; savePending(); }
                else return;
                if (stopped) return;
            }
            if (await post(body(eventType, kind)) === 'network') remember(eventType, kind);
        });
    }
    function leave(kind) {
        if (stopped) return;
        if (!away || kind === 1) { away = true; send('start', kind); }
    }
    function back() {
        if (!document.hidden && document.hasFocus() && !exiting) {
            if (away) { away = false; send('end'); }
        }
    }
    document.addEventListener('visibilitychange', () => document.hidden ? leave(1) : back());
    // Bấm/cuộn trong khung đề PDF (iframe ngay trong trang, bài làm theo đề) cũng làm cửa sổ "blur" dù học viên vẫn ở trang:
    // không tính là rời. Trong lúc con trỏ ở khung đề thì hỏi document.hasFocus() (true cả khi khung con đang giữ con trỏ)
    // để vẫn bắt được việc chuyển sang ứng dụng khác và lúc quay lại.
    let frameWatch = null;
    window.addEventListener('blur', () => setTimeout(() => {
        if (document.activeElement?.tagName !== 'IFRAME') { leave(2); return; }
        clearInterval(frameWatch);
        frameWatch = setInterval(() => {
            if (document.activeElement?.tagName !== 'IFRAME') { clearInterval(frameWatch); return; }
            if (!document.hasFocus()) leave(2); else back();
        }, 500);
    }, 0));
    window.addEventListener('focus', back);
    window.addEventListener('offline', () => setOffline(true));
    window.addEventListener('online', () => send(away ? 'heartbeat' : 'end')); // thử ngay, chỉ mở đề khi máy chủ nhận được.
    if (!navigator.onLine) setOffline(true);
    function exit() {
        if (exiting || stopped || root.inert) return;
        exiting = true;
        const data = body('start', 3);
        if (!navigator.sendBeacon(url, data)) fetch(url, { method: 'POST', body: data, keepalive: true }).catch(() => {});
    }
    window.addEventListener('beforeunload', () => {
        exit();
        // Điều hướng bị hủy thì trang vẫn phải tiếp tục gửi tín hiệu.
        setTimeout(() => { if (!document.hidden && !stopped) { exiting = false; send('end'); } }, 1000);
    });
    window.addEventListener('pagehide', exit);
    window.addEventListener('pageshow', () => { exiting = false; send('end'); back(); });
    // (H2) đóng lần rời trang trước khi tải lại, nếu có; không tự tạo một lần rời mới.
    send('end');
    if (document.hidden) leave(1);
    const heartbeat = setInterval(() => {
        if (stopped || root.inert) { clearInterval(heartbeat); return; }
        if (exiting) return;
        send(away ? 'heartbeat' : 'end'); // bù lần quay lại bị mất khi mạng gián đoạn.
    }, 15000); // (D, người dùng duyệt 14/11/2026) 15 giây/lần; mốc mất tín hiệu vẫn 45 giây.
})();
