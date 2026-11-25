// (63, 66, 69, B4a) thông báo chỉ gợi ý; máy chủ vẫn kiểm hạn và thiết bị cho mọi thao tác.
(() => {
    const root = document.getElementById('play');
    if (!root || typeof signalR === 'undefined') return;
    const status = document.getElementById('connection-status');
    const dialog = document.getElementById('learn-notice');
    const modal = new bootstrap.Modal(dialog, { backdrop: 'static', keyboard: true });
    let redirect = null, previousFocus = null, stopped = false;
    function notice(message, url = null, lock = false) {
        if (root.inert && !lock) return;
        if (lock) root.inert = true;
        redirect = url;
        if (!dialog.contains(document.activeElement)) previousFocus = document.activeElement;
        document.getElementById('learn-notice-message').textContent = message;
        document.getElementById('learn-notice-ok').textContent = lock ? (url === root.dataset.assignmentUrl ? 'Về bài này' : 'Xem lại bài') : 'Đã hiểu';
        modal.show();
    }
    dialog.addEventListener('shown.bs.modal', () => {
        dialog.setAttribute('role', 'alertdialog');
        document.getElementById('learn-notice-ok').focus();
    });
    dialog.addEventListener('hidden.bs.modal', () => {
        // (19) cùng địa chỉ (kể cả #câu) thì assign chỉ cuộn, không tải lại; phải reload để nhận bản đề mới.
        if (redirect === location.href) location.reload();
        else if (redirect) location.assign(redirect);
        else previousFocus?.focus();
    });
    function moved() {
        notice('Bài đã chuyển sang thiết bị khác.', root.dataset.assignmentUrl, true);
    }
    async function readState() {
        const response = await fetch(root.dataset.stateUrl, { headers: { Accept: 'application/json' }, cache: 'no-store' });
        const state = await response.json();
        if (!response.ok) throw new Error(state.error || 'Không lấy được trạng thái bài.');
        root.dispatchEvent(new CustomEvent('learn:state', { detail: state }));
        return state;
    }
    root.addEventListener('learn:state', event => {
        if (event.detail.moved && !event.detail.ended) moved();
        else if (event.detail.ended && !root.inert) notice('Lượt của bạn đã kết thúc.', event.detail.reviewUrl, true);
        else if (event.detail.questionVersions && [...root.querySelectorAll('.question')].some(q =>
            Number(q.dataset.questionVersion) !== event.detail.questionVersions[q.dataset.id])) edited();
    });
    const connection = new signalR.HubConnectionBuilder().withUrl(root.dataset.hubUrl)
        .withAutomaticReconnect([0, 2000, 10000, 30000]).build();
    connection.on('moved', moved);
    connection.on('ended', event => notice('Lượt của bạn đã kết thúc.', event.url, true)); // (H8)
    function edited() {
        notice('Giảng viên vừa sửa câu hỏi hoặc đáp án. Bấm Tải lại để thấy bản mới; câu nào bị sửa thì bạn trả lời lại câu đó.', location.href, true);
        document.getElementById('learn-notice-ok').textContent = 'Tải lại';
    }
    connection.on('question-edited', edited);
    connection.on('closed', event => notice('Giảng viên đã đóng bài. Lượt của bạn đã kết thúc.', event.url, true));
    connection.on('content-hidden', event => notice('Nội dung bài đã bị quản trị viên ẩn. Lượt của bạn đã kết thúc.', event.url, true));
    connection.on('removed', event => notice('Bạn đã rời hoặc bị xóa khỏi lớp. Lượt của bạn đã kết thúc.', event.url, true));
    connection.on('account-locked', event => {
        notice('Tài khoản của bạn đã bị khóa. Lượt làm bài đã dừng.', event.url, true);
        document.getElementById('learn-notice-ok').textContent = 'Đăng nhập';
    });
    connection.on('deadline-changed', async () => {
        try {
            const state = await readState();
            if (!state.ended && !state.moved) notice(state.time ? 'Giảng viên đã đổi hạn thành ' + state.time : 'Giảng viên đã bỏ hạn nộp.');
        } catch (error) { status.textContent = error.message; }
    });
    async function join() {
        try {
            await connection.invoke('JoinAttempt', Number(root.dataset.attempt));
            status.textContent = '';
            await readState(); // (63) lấy lại hạn nếu popup bị lỡ khi mất mạng.
            root.dispatchEvent(new CustomEvent('learn:connected')); // (59) tiếp tục giờ câu Ôn tập đã dừng khi ngắt.
        } catch (error) {
            status.textContent = 'Không vào được phòng bài làm. Hãy kiểm tra kết nối hoặc tải lại trang.';
            try { await readState(); } catch { /* Thao tác làm bài vẫn được máy chủ kiểm tra. */ }
        }
    }
    async function start() {
        if (stopped || root.inert) return;
        try { await connection.start(); await join(); }
        catch {
            status.textContent = 'Mất kết nối thông báo. Đang thử kết nối lại.';
            setTimeout(start, 5000);
        }
    }
    connection.onreconnecting(() => { status.textContent = 'Đang kết nối lại thông báo.'; });
    connection.onreconnected(join);
    connection.onclose(() => { if (!stopped) setTimeout(start, 5000); });
    window.addEventListener('pagehide', () => { stopped = true; connection.stop(); });
    document.addEventListener('submit', event => {
        if (root.inert && root.contains(event.target)) event.preventDefault();
    }, true);
    start();
})();
