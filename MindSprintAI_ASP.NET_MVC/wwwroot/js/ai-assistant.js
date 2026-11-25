(() => {
    'use strict';

    // The model and source are untrusted text: construct text nodes, never HTML.
    function textElement(tag, text, classes) {
        const element = document.createElement(tag);
        element.textContent = text || '';
        if (classes) element.className = classes;
        return element;
    }

    document.querySelectorAll('.ai-assistant').forEach(root => {
        const form = root.querySelector('.ai-compose');
        const status = root.querySelector('.ai-status');
        const messages = root.querySelector('.ai-messages');
        const history = root.querySelector('.ai-history');
        const privateHistory = root.querySelector('.ai-private-history');
        const keyHelp = root.querySelector('.ai-key-help');
        const source = root.querySelector('.ai-source');
        let permitted = false;
        let historyAllowed = false;
        let busy = false;
        let epoch = 0;

        async function json(url, options) {
            const response = await fetch(url, { credentials: 'same-origin', ...options, headers: { Accept: 'application/json', ...options?.headers } });
            const data = await response.json().catch(() => null);
            if (!response.ok) throw new Error(data?.error || data?.message || 'Không thể mở trợ lý lúc này. Vui lòng thử lại.');
            return data;
        }

        function render(conversation) {
            messages.replaceChildren();
            form.elements.conversationId.value = conversation.id;
            (conversation.messages || []).forEach(message => {
                const article = textElement('article', '', 'border rounded p-3 mb-2');
                article.append(textElement('h3', message.role === 'user' ? 'Bạn' : 'Trợ lý', 'h6'));
                const content = textElement('p', message.content, 'mb-2');
                content.style.whiteSpace = 'pre-wrap';
                content.style.overflowWrap = 'anywhere';
                article.append(content);
                (message.citations || []).forEach((citation, index) => {
                    const quote = textElement('blockquote', citation.excerptText, 'small border-start ps-2 mb-1');
                    quote.style.whiteSpace = 'pre-wrap';
                    article.append(quote);
                    const link = textElement('a', `Xem đoạn trích ${index + 1} trong văn bản nguồn`, 'btn btn-sm btn-outline-secondary me-1 mb-2');
                    const url = new URL(root.dataset.sourceUrl, location.origin);
                    url.search = new URLSearchParams({ id: root.dataset.assignment, start: citation.startOffset, end: citation.endOffset, version: message.sourceVersion });
                    url.hash = `source-${citation.startOffset}`;
                    link.href = url.href;
                    article.append(link);
                });
                if (message.role === 'assistant') {
                    const report = textElement('a', 'Báo cáo câu trả lời', 'small d-block');
                    report.href = root.dataset.reportUrl + '?' + new URLSearchParams({ targetType: 'AiConversation', targetId: conversation.id });
                    article.append(report);
                }
                messages.append(article);
            });
        }

        async function loadHistory() {
            const requestEpoch = epoch;
            const conversations = await json(root.dataset.historyUrl);
            if (!historyAllowed || requestEpoch !== epoch) return;
            const selected = history.value;
            history.replaceChildren(new Option('Hội thoại mới', ''));
            conversations.forEach(item => history.add(new Option(item.title, item.id)));
            history.value = selected;
        }

        async function refresh() {
            try {
                const state = await json(root.dataset.stateUrl);
                permitted = !!state.allowed && !!state.hasKey;
                historyAllowed = !state.locked;
                // Khóa 71: không có gì dùng được nên ẩn cả nút, tránh làm học viên phân tâm khi đang Kiểm tra.
                // Đóng ngăn kéo trước khi ẩn, nếu không lớp phủ (backdrop) của Bootstrap còn che trang.
                if (state.locked) window.bootstrap?.Offcanvas.getInstance(root.querySelector('.offcanvas'))?.hide();
                root.classList.toggle('d-none', !!state.locked);
                const title = state.allowed ? 'Hỏi trợ lý' : 'Lịch sử trợ lý';
                root.querySelector('[data-bs-toggle="offcanvas"]').textContent = title;
                root.querySelector('.ai-drawer-title').textContent = state.allowed ? 'Trợ lý AI' : 'Lịch sử trợ lý AI'; // pháp lý C6
                form.classList.toggle('d-none', !permitted);
                privateHistory.classList.toggle('d-none', !historyAllowed);
                source.classList.toggle('d-none', !state.sourceAvailable || !!state.locked);
                keyHelp.classList.toggle('d-none', !!state.locked || !!state.hasKey);
                status.textContent = state.reason || (state.allowed ? 'Bạn có thể hỏi trợ lý theo văn bản nguồn.' : 'Hiện chưa được hỏi trợ lý. Bạn vẫn xem được lịch sử của mình.');
                if (!historyAllowed) {
                    epoch++;
                    messages.replaceChildren();
                    history.replaceChildren(new Option('Hội thoại mới', ''));
                    form.elements.conversationId.value = '';
                } else await loadHistory();
            } catch (error) {
                permitted = historyAllowed = false;
                epoch++;
                [form, privateHistory, source, keyHelp].forEach(element => element.classList.add('d-none'));
                messages.replaceChildren();
                status.textContent = error.message;
            }
        }

        history.addEventListener('change', async () => {
            if (!historyAllowed) return;
            form.elements.conversationId.value = history.value;
            messages.replaceChildren();
            if (!history.value) return;
            const requestEpoch = epoch;
            const requestedId = history.value;
            try {
                const conversation = await json(root.dataset.conversationUrl + '?' + new URLSearchParams({ id: requestedId }));
                if (historyAllowed && requestEpoch === epoch && history.value === requestedId) render(conversation);
            } catch (error) { status.textContent = error.message; await refresh(); }
        });

        form.addEventListener('submit', async event => {
            event.preventDefault();
            if (!permitted || busy || !form.reportValidity()) return;
            busy = true;
            const button = form.querySelector('button');
            button.disabled = true;
            status.textContent = 'Trợ lý đang đọc nguồn…';
            const requestEpoch = epoch;
            try {
                const conversation = await json(form.action, { method: 'POST', body: new FormData(form) });
                // A setting change or Test lock can arrive while Gemini is answering.
                await refresh();
                if (historyAllowed && requestEpoch === epoch) {
                    render(conversation);
                    await loadHistory();
                    history.value = conversation.id;
                    form.elements.question.value = '';
                    status.textContent = 'Đã nhận câu trả lời.';
                    form.elements.question.focus();
                }
            } catch (error) { await refresh(); status.textContent = error.message; }
            finally { busy = false; button.disabled = false; }
        });

        root.querySelector('.offcanvas').addEventListener('shown.bs.offcanvas', () => {
            if (permitted) form.elements.question.focus();
            else if (historyAllowed) history.focus();
            else status.focus();
            refresh();
        });
        refresh();

        if (window.signalR) {
            const connection = new signalR.HubConnectionBuilder().withUrl(root.dataset.hubUrl).withAutomaticReconnect().build();
            const join = () => connection.invoke('JoinAi', Number(root.dataset.assignment));
            connection.on('ai-settings-changed', refresh);
            connection.onreconnected(async () => { await join(); await refresh(); });
            connection.start().then(join).catch(() => { /* Recheck on focus and periodically if push is unavailable. */ });
            window.addEventListener('pagehide', () => connection.stop(), { once: true });
        }
        window.addEventListener('focus', refresh);
        window.setInterval(() => { if (!document.hidden) refresh(); }, 30000);
    });

    const sourceText = document.getElementById('source-text');
    if (sourceText) {
        async function checkSourceLock() {
            try {
                const response = await fetch(sourceText.dataset.stateUrl, { credentials: 'same-origin' });
                const state = response.ok ? await response.json() : null;
                if (!state || state.locked || !state.sourceAvailable) {
                    sourceText.replaceChildren(textElement('p', state?.reason || 'Hiện không được đọc văn bản nguồn. Tải lại trang khi lượt Kiểm tra đã kết thúc.', 'alert alert-warning'));
                }
            } catch { sourceText.replaceChildren(textElement('p', 'Không thể kiểm tra quyền đọc nguồn. Vui lòng tải lại trang.', 'alert alert-warning')); }
        }
        window.addEventListener('focus', checkSourceLock);
        window.setInterval(checkSourceLock, 30000);
        const query = new URLSearchParams(location.search);
        const start = Number(query.get('start'));
        const end = Number(query.get('end'));
        const version = Number(query.get('version'));
        if (query.has('version') && version !== Number(sourceText.dataset.version)) {
            document.getElementById('source-version-notice').classList.remove('d-none');
        } else if (query.has('start') && Number.isInteger(start) && Number.isInteger(end) && start >= 0 && end > start) {
            let firstMark;
            sourceText.querySelectorAll('[data-start]').forEach(paragraph => {
                const content = paragraph.textContent;
                const paragraphStart = Number(paragraph.dataset.start);
                const from = Math.max(0, start - paragraphStart);
                const to = Math.min(content.length, end - paragraphStart);
                if (from >= to) return;
                const mark = textElement('mark', content.slice(from, to));
                paragraph.replaceChildren(document.createTextNode(content.slice(0, from)), mark, document.createTextNode(content.slice(to)));
                firstMark ||= mark;
            });
            if (firstMark) { firstMark.scrollIntoView({ block: 'center' }); sourceText.focus({ preventScroll: true }); }
        }
    }
})();
