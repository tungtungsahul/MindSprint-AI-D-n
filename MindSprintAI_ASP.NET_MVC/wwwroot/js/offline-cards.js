// Ôn thẻ khi mất mạng (FR-21/FR-22 của nhóm; người dùng chốt 15/11): đánh giá thẻ chưa gửi được thì lưu tạm trong trình duyệt,
// có mạng lại thì gửi bù theo đúng thứ tự tới đúng địa chỉ chấm thẻ (máy chủ vẫn kiểm hạn nộp… lúc nhận).
// - Hàng đợi gắn với tài khoản đang đăng nhập (data-user): đổi tài khoản trên cùng máy không gửi nhầm của người trước.
// - Nhiều tab: Web Locks cho một tab gửi một lúc.
// - Máy chủ từ chối một đánh giá (vd. đã quá hạn) → bỏ đánh giá đó và báo; mất mạng / hết phiên đăng nhập → giữ lại, thử sau.
// shortcut: mất kết nối đúng lúc máy chủ vừa lưu xong (chưa kịp trả lời) thì đánh giá đó có thể được gửi lại một lần nữa;
//           muốn chặn tuyệt đối thì cần mã yêu cầu riêng cho mỗi lần chấm thẻ ở máy chủ.
(() => {
    const user = document.currentScript?.dataset.user;
    if (!user) return;
    const key = `mindsprint-offline-cards-${user}`;
    const read = () => { try { return JSON.parse(localStorage.getItem(key)) ?? []; } catch { return []; } };
    const write = list => { try { list.length ? localStorage.setItem(key, JSON.stringify(list)) : localStorage.removeItem(key); } catch { } };
    const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    const say = text => {
        let box = document.getElementById('offline-cards-toast');
        if (!box) {
            box = document.createElement('p'); box.id = 'offline-cards-toast'; box.className = 'offline-toast'; box.setAttribute('role', 'status');
            document.body.append(box);
        }
        box.textContent = text; box.hidden = false;
        clearTimeout(box.timer); box.timer = setTimeout(() => { box.hidden = true; }, 8000);
    };

    let busy = false;
    const sendAll = async () => {
        let sent = 0, last = null;
        const dropped = [];
        for (let list = read(); list.length; list = read()) {
            const item = list[0], tok = token();
            if (!tok) break;
            const form = new FormData();
            for (const [k, v] of Object.entries(item.fields)) form.set(k, v ?? '');
            form.set('__RequestVerificationToken', tok);
            let res;
            try { res = await fetch(item.url, { method: 'POST', body: form, headers: { Accept: 'application/json' } }); } catch { break; } // vẫn mất mạng
            const json = res.headers.get('content-type')?.includes('json') ? await res.json().catch(() => null) : null;
            if (res.status >= 500 || (res.ok && !json)) break; // máy chủ lỗi, hoặc hết phiên (bị đưa về trang đăng nhập): giữ hàng đợi
            if (res.ok) { sent++; last = { result: json, fields: item.fields }; }
            else dropped.push(json?.error || 'Không lưu được đánh giá.'); // 4xx: máy chủ từ chối hẳn (quá hạn, không có quyền…)
            write(read().slice(1));
        }
        return { sent, dropped, last, pending: read().length };
    };

    const flush = async () => {
        if (busy || !navigator.onLine || !read().length) return;
        busy = true;
        try {
            const report = navigator.locks ? await navigator.locks.request(key, sendAll) : await sendAll();
            const parts = [];
            if (report.sent) parts.push(`Đã gửi ${report.sent} đánh giá thẻ ôn lúc mất mạng.`);
            if (report.dropped.length) parts.push(`${report.dropped.length} đánh giá không lưu được: ${report.dropped[0]}`);
            if (parts.length) say(parts.join(' '));
            window.dispatchEvent(new CustomEvent('mindsprint-offline-cards', { detail: report }));
        } finally { busy = false; }
    };

    window.MindSprintOfflineCards = {
        pending: () => read().length,
        enqueue: (url, fields) => { write([...read(), { url, fields }]); window.dispatchEvent(new CustomEvent('mindsprint-offline-cards', { detail: { pending: read().length } })); },
        flush
    };
    window.addEventListener('online', flush);
    document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') flush(); });
    flush();
})();
