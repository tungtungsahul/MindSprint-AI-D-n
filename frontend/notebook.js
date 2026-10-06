// Sổ tay AI - các tính năng học tập kiểu NotebookLM:
// nguồn tài liệu (dán/PDF/TXT/URL), hỏi đáp có trích dẫn, tóm tắt, study guide, FAQ,
// timeline, mind map, flashcard, trắc nghiệm, audio overview, ghi chú.
(function () {
    const pane = document.getElementById('tab-content-notebook');
    const api = window.MindSprintApi;
    const auth = window.MindSprintAuth;
    if (!pane || !api || !auth) return;

    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const $ = (sel) => pane.querySelector(sel);

    const emptyState = () => ({ notebooks: [], nb: null, sources: [], notes: [], history: [], quiz: null, audio: null, lastText: null, lastTitle: '', lastCards: null });
    const state = emptyState();
    let workspaceVersion = 0;
    let notebookRequest = 0;
    let chatPending = false;
    let studioPending = false;
    const MAX_FILE = 10 * 1024 * 1024;

    // ---------- helpers ----------
    const srcTitle = (n) => (state.sources[Number(n) - 1] || {}).title || 'Nguồn ' + n;

    function md(src) {
        return window.MindSprintText.markdown(src, srcTitle);
    }
    function busy(btn, on) { if (btn) { btn.disabled = on; btn.style.opacity = on ? 0.6 : 1; } }
    function aiError(error) {
        let message = error.message || 'AI chưa xử lý được yêu cầu. Vui lòng thử lại.';
        const seconds = Number(error.retryAfterSeconds);
        if (error.code === 'ai_rate_limited' && seconds > 0) {
            const wait = seconds >= 3600 ? Math.ceil(seconds / 3600) + ' giờ' : Math.ceil(seconds / 60) + ' phút';
            message += ` Thời gian chờ dự kiến: ${wait}.`;
        }
        return `<span>${esc(message)}</span>`;
    }
    async function run(btn, fn) {
        busy(btn, true);
        if (btn?.dataset.act === 'add-url') showUrlFeedback('Đang tải và đọc nguồn từ URL...');
        try { return await fn(); } catch (e) {
            if (btn?.dataset.act === 'add-url') {
                if (!btn.isConnected) return;
                const blocked = /^Trang trả về lỗi 403\b/.test(e.message || '');
                const redirected = /^Trang trả về lỗi 30[12378]\b/.test(e.message || '');
                const message = blocked
                    ? 'Trang nguồn từ chối cho Sổ tay AI tải nội dung (403). Trang có thể yêu cầu đăng nhập hoặc chặn truy cập tự động.'
                    : redirected
                        ? 'Đường dẫn này chuyển sang một trang khác. Mở đường dẫn trong trình duyệt rồi thử URL cuối cùng trên thanh địa chỉ.'
                        : e.message || 'Không tải được nội dung từ URL. Vui lòng thử lại.';
                showUrlFeedback(message, true, e.status !== 401);
            } else alert(e.message || 'Có lỗi xảy ra');
        } finally { busy(btn, false); }
    }
    function showUrlFeedback(message, error = false, alternatives = false) {
        const feedback = $('#nb-url-feedback');
        if (!feedback) return;
        feedback.hidden = false;
        feedback.classList.toggle('is-error', error);
        $('#nb-url').setAttribute('aria-invalid', String(error));
        feedback.innerHTML = `<p>${esc(message)}</p>${alternatives ? `<p class="nb-muted">Bạn vẫn có thể mở trang để sao chép nội dung rồi dán vào sổ tay, hoặc tải tài liệu và thêm bằng file.</p>
            <div class="nb-url-alternatives"><button class="btn btn-secondary" type="button" data-ui-act="focus-paste">Dán văn bản</button><button class="btn btn-secondary" type="button" data-ui-act="focus-upload">Tải file</button></div>` : ''}`;
    }
    function ensureLoggedIn() {
        if (auth.isLoggedIn()) return true;
        const status = $('#nb-status');
        status.textContent = 'Đăng nhập bằng tài khoản chung ở thanh bên để lưu dữ liệu sổ tay.';
        status.hidden = false;
        return false;
    }

    // ---------- render: giao diện chính ----------
    function renderMain() {
        pane.innerHTML = `
        <header class="main-header">
            <div class="header-info"><h1>Sổ tay AI</h1><p>Thêm tài liệu, hỏi đáp có trích dẫn và tạo tài liệu ôn tập từ chính nguồn của bạn.</p></div>
        </header>
        <p id="nb-status" class="nb-err" role="status" hidden></p>
        <div class="nb-layout">
            <section class="nb-col nb-sources glass-panel">
                <h3><span><i class="fas fa-folder-open"></i> Sổ tay & Nguồn</span><button class="nb-source-toggle nb-icon-btn" type="button" data-ui-act="toggle-sources" aria-label="Mở hoặc thu gọn nguồn tài liệu" aria-expanded="false" aria-controls="nb-source-content"><i class="fas fa-chevron-down" aria-hidden="true"></i></button></h3>
                <div class="nb-source-content" id="nb-source-content">
                <div class="nb-row">
                    <select id="nb-select">${state.notebooks.map(n => `<option value="${n.id}" ${state.nb && n.id === state.nb.id ? 'selected' : ''}>${esc(n.title)}</option>`).join('')}</select>
                    <button class="nb-icon-btn" data-act="nb-new" title="Sổ tay mới"><i class="fas fa-plus"></i></button>
                    <button class="nb-icon-btn" data-act="nb-del" title="Xóa sổ tay"><i class="fas fa-trash-alt"></i></button>
                </div>
                <div id="nb-source-list"></div>
                <details><summary><i class="fas fa-paste"></i> Dán văn bản</summary>
                    <input type="text" id="nb-txt-title" placeholder="Tiêu đề" maxlength="100">
                    <textarea id="nb-txt-body" rows="5" placeholder="Dán nội dung bài giảng, ghi chú..."></textarea>
                    <button class="btn btn-secondary" data-act="add-text">Thêm nguồn</button></details>
                <details><summary><i class="fas fa-file-upload"></i> Tải file (PDF, TXT, MD)</summary>
                    <input type="file" id="nb-file" accept=".pdf,.txt,.md,.csv">
                    <button class="btn btn-secondary" data-act="add-file">Tải lên</button></details>
                <details><summary><i class="fas fa-link"></i> Thêm từ URL</summary>
                    <input type="url" id="nb-url" placeholder="https://..." aria-label="Đường dẫn trang nguồn" aria-describedby="nb-url-hint nb-url-feedback">
                    <p class="nb-muted nb-url-hint" id="nb-url-hint">Hỗ trợ web, PDF có chữ, DOCX/PPTX/XLSX, TXT/MD/CSV và JSON/XML/RSS. Dùng URL công khai; một số trang yêu cầu đăng nhập hoặc chặn tải tự động.</p>
                    <button class="btn btn-secondary" data-act="add-url">Thêm</button>
                    <div class="nb-url-feedback" id="nb-url-feedback" role="status" aria-live="polite" aria-atomic="true" hidden></div></details>
                </div>
            </section>

            <section class="nb-col nb-chat glass-panel">
                <h3><i class="fas fa-comments"></i> Hỏi đáp theo nguồn</h3>
                <div class="nb-welcome">
                    <div class="welcome-orb" aria-hidden="true"></div>
                    <h2>Bạn muốn khám phá điều gì từ tài liệu?</h2>
                    <p>Cùng AI đọc hiểu, kết nối kiến thức và chuẩn bị cho lần ôn tập tiếp theo.</p>
                </div>
                <div class="nb-messages" id="nb-messages"><div class="nb-muted">Thêm nguồn ở bên trái, rồi đặt câu hỏi. Câu trả lời chỉ dựa trên tài liệu của bạn và có số trích dẫn <span class="nb-cite">1</span>.</div></div>
                <div class="nb-quick-actions" aria-label="Thao tác AI nhanh">
                    <button class="nb-chip" type="button" data-act="suggest"><i class="fas fa-lightbulb" aria-hidden="true"></i> Gợi ý câu hỏi</button>
                    <button class="nb-chip" type="button" data-gen="summary"><i class="fas fa-file-alt" aria-hidden="true"></i> Tóm tắt</button>
                    <button class="nb-chip" type="button" data-gen="flashcards"><i class="fas fa-layer-group" aria-hidden="true"></i> Flashcard</button>
                    <button class="nb-chip" type="button" data-gen="quiz"><i class="fas fa-edit" aria-hidden="true"></i> Trắc nghiệm</button>
                </div>
                <div class="nb-suggest" id="nb-suggest"></div>
                <div class="nb-composer">
                    <textarea class="nb-input" id="nb-q" aria-label="Câu hỏi về tài liệu" rows="3" placeholder="Bạn muốn hỏi gì về tài liệu của mình?" maxlength="1000"></textarea>
                    <div class="nb-composer-footer">
                        <button class="nb-icon-btn" type="button" data-ui-act="focus-upload" aria-label="Mở phần tải tài liệu" title="Thêm tài liệu"><i class="fas fa-plus" aria-hidden="true"></i></button>
                        <span>Hỏi đáp dựa trên nguồn của bạn</span>
                        <button class="btn btn-primary" data-act="send" aria-label="Gửi câu hỏi">Gửi câu hỏi <i class="fas fa-arrow-right" aria-hidden="true"></i></button>
                    </div>
                </div>
            </section>

            <section class="nb-col nb-studio glass-panel">
                <h3><i class="fas fa-magic"></i> Studio</h3>
                <div class="nb-studio-grid">
                    <button data-gen="summary"><i class="fas fa-file-alt" aria-hidden="true"></i><span>Tóm tắt</span><small>Nắm nhanh các ý chính</small></button>
                    <button data-gen="studyguide"><i class="fas fa-graduation-cap" aria-hidden="true"></i><span>Study guide</span><small>Lập hướng dẫn ôn tập</small></button>
                    <button data-gen="faq"><i class="fas fa-question-circle" aria-hidden="true"></i><span>FAQ</span><small>Giải đáp điều cần nhớ</small></button>
                    <button data-gen="timeline"><i class="fas fa-stream" aria-hidden="true"></i><span>Timeline</span><small>Kết nối các mốc sự kiện</small></button>
                    <button data-gen="mindmap"><i class="fas fa-project-diagram" aria-hidden="true"></i><span>Mind map</span><small>Hệ thống hóa kiến thức</small></button>
                    <button data-gen="flashcards"><i class="fas fa-layer-group" aria-hidden="true"></i><span>Flashcard</span><small>Ghi nhớ qua bộ thẻ</small></button>
                    <button data-gen="quiz"><i class="fas fa-edit" aria-hidden="true"></i><span>Trắc nghiệm</span><small>Kiểm tra điều đã hiểu</small></button>
                    <button data-gen="audio"><i class="fas fa-podcast" aria-hidden="true"></i><span>Audio overview</span><small>Nghe lại nội dung học</small></button>
                </div>
                <div class="nb-studio-results">
                <section class="nb-result-panel" aria-label="Kết quả Studio">
                <input class="nb-input" id="nb-focus" type="text" placeholder="Trọng tâm (tuỳ chọn), vd: chương 3" maxlength="200" autocomplete="off">
                <div id="nb-studio-status" role="status" hidden></div>
                <div class="nb-out" id="nb-out"><div class="nb-muted">Kết quả sẽ hiển thị ở đây.</div></div>
                </section>
                <section class="nb-notes-panel" aria-label="Ghi chú đã lưu">
                <h3><i class="fas fa-sticky-note"></i> Ghi chú</h3>
                <div id="nb-notes"></div>
                </section>
                </div>
            </section>
        </div>`;
        renderSources(); renderNotes();
    }

    function renderSources() {
        const el = $('#nb-source-list'); if (!el) return;
        el.innerHTML = state.sources.length
            ? state.sources.map((s, i) => `<div class="nb-source"><span class="nb-src-n">${i + 1}</span>
                <i class="fas ${s.type === 'pdf' ? 'fa-file-pdf' : s.type === 'url' ? 'fa-link' : 'fa-file-alt'}"></i>
                <span class="nb-src-t" title="${esc(s.title)}">${esc(s.title)}</span>
                <button class="nb-icon-btn" data-act="src-del" data-id="${s.id}" title="Xóa"><i class="fas fa-times"></i></button></div>`).join('')
            : '<div class="nb-muted">Chưa có nguồn nào.</div>';
    }
    function renderNotes() {
        const el = $('#nb-notes'); if (!el) return;
        el.innerHTML = state.notes.length
            ? state.notes.map(n => `<div class="nb-note"><span data-act="note-open" data-id="${n.id}" title="${esc(n.title)}">${esc(n.title)}</span>
                <button class="nb-icon-btn" data-act="note-del" data-id="${n.id}"><i class="fas fa-trash-alt"></i></button></div>`).join('')
            : '<div class="nb-muted">Chưa có ghi chú. Lưu câu trả lời hoặc tài liệu từ Studio.</div>';
    }

    // ---------- dữ liệu ----------
    function resetWorkspace() {
        workspaceVersion++;
        notebookRequest++;
        chatPending = false; studioPending = false;
        stopAudio();
        Object.assign(state, emptyState());
        renderMain();
    }

    async function loadNotebook(id) {
        const version = workspaceVersion;
        const request = ++notebookRequest;
        const notebook = state.notebooks.find(n => n.id === Number(id)) || null;
        if (!notebook) return false;
        const [sources, notes] = await Promise.all([api.nbSources(notebook.id), api.nbNotes(notebook.id)]);
        if (version !== workspaceVersion || request !== notebookRequest || !auth.isLoggedIn()) return false;
        state.nb = notebook;
        workspaceVersion++;
        chatPending = false; studioPending = false;
        state.history = [];
        state.sources = sources;
        state.notes = notes;
        return true;
    }

    async function start() {
        const version = workspaceVersion;
        if (!auth.isLoggedIn()) {
            resetWorkspace();
            return;
        }
        try {
            let notebooks = await api.nbList();
            if (version !== workspaceVersion || !auth.isLoggedIn()) return;
            if (!notebooks.length) notebooks = [await api.nbCreate('Sổ tay đầu tiên')];
            if (version !== workspaceVersion || !auth.isLoggedIn()) return;
            state.notebooks = notebooks;
            if (!await loadNotebook(state.nb ? state.nb.id : notebooks[0].id)) return;
            renderMain();
        } catch (e) {
            if (version !== workspaceVersion) return;
            if (e.message?.includes('401') || e.message?.includes('hết hạn')) {
                resetWorkspace();
            } else {
                alert(e.message || 'Có lỗi xảy ra');
            }
        }
    }
    async function refreshSources() {
        const version = workspaceVersion, id = state.nb.id;
        const sources = await api.nbSources(id);
        if (version === workspaceVersion && state.nb?.id === id) { state.sources = sources; renderSources(); }
    }
    async function refreshNotes() {
        const version = workspaceVersion, id = state.nb.id;
        const notes = await api.nbNotes(id);
        if (version === workspaceVersion && state.nb?.id === id) { state.notes = notes; renderNotes(); }
    }

    // ---------- chat ----------
    function addMsg(role, html) {
        const box = $('#nb-messages');
        if (box.querySelector('.nb-muted') && box.children.length === 1) box.innerHTML = '';
        const d = document.createElement('div'); d.className = 'nb-msg ' + (role === 'user' ? 'user' : 'bot'); d.innerHTML = html;
        box.appendChild(d); box.scrollTop = box.scrollHeight; return d;
    }
    async function sendQuestion(q, btn) {
        if (chatPending) return;
        if (!ensureLoggedIn() || !state.nb) return;
        q = (q || '').trim(); if (!q) return;
        if (!state.sources.length) return alert('Hãy thêm ít nhất một nguồn trước.');
        const version = workspaceVersion, notebookId = state.nb.id;
        chatPending = true;
        $('#nb-q').value = '';
        addMsg('user', esc(q));
        const wait = addMsg('bot', '<span class="nb-muted" role="status"><span class="nb-loading-dots" aria-hidden="true"><i></i><i></i><i></i></span> Đang đọc nguồn...</span>');
        busy(btn, true);
        busy($('[data-act="send"]'), true);
        busy($('#nb-q'), true);
        try {
            const r = await api.nbChat(notebookId, q, state.history);
            if (version !== workspaceVersion || state.nb?.id !== notebookId) return;
            state.history.push({ role: 'user', text: q }, { role: 'assistant', text: r.answer });
            const quotes = (r.citations || []).filter(c => c.quote).map(c => `<div><span class="nb-cite">${c.source}</span> <strong>${esc(srcTitle(c.source))}</strong>: “${esc(c.quote)}”</div>`).join('');
            wait.innerHTML = md(r.answer) + (quotes ? `<div class="nb-quotes">${quotes}</div>` : '') +
                `<button class="nb-icon-btn" data-act="save-answer" style="margin-top:var(--space-sm)"><i class="fas fa-bookmark"></i> Lưu ghi chú</button>`;
            wait.dataset.raw = r.answer; wait.dataset.q = q;
        } catch (e) {
            if (version !== workspaceVersion || state.nb?.id !== notebookId) return;
            wait.innerHTML = `<div class="nb-ai-error" role="status">${aiError(e)}
                <button class="btn btn-secondary" data-act="retry-chat" data-q="${esc(q)}">Thử lại câu hỏi</button></div>`;
        } finally {
            if (version === workspaceVersion) {
                chatPending = false; busy(btn, false);
                busy($('[data-act="send"]'), false); busy($('#nb-q'), false);
            }
        }
    }

    // ---------- Studio ----------
    async function generate(type, btn) {
        if (studioPending || !ensureLoggedIn() || !state.nb) return;
        if (!state.sources.length) return alert('Hãy thêm ít nhất một nguồn trước.');
        stopAudio();
        const version = workspaceVersion, notebookId = state.nb.id;
        const out = $('#nb-out'), status = $('#nb-studio-status');
        const previous = { lastText: state.lastText, lastCards: state.lastCards, lastTitle: state.lastTitle, quiz: state.quiz, audio: state.audio };
        status.hidden = false;
        status.className = 'nb-muted';
        status.innerHTML = '<span class="nb-loading-dots" aria-hidden="true"><i></i><i></i><i></i></span> AI đang tạo, tự thử lại nếu dịch vụ bận...';
        studioPending = true;
        pane.querySelectorAll('[data-gen], [data-act="retry-generate"]').forEach(button => busy(button, true));
        try {
            const r = await api.nbGenerate(notebookId, type, $('#nb-focus').value.trim(), 10);
            if (version !== workspaceVersion || state.nb?.id !== notebookId) return;
            status.hidden = true;
            state.lastText = null; state.lastCards = null;
            const label = btn.textContent.trim();
            if (!r.isJson) {
                state.lastText = r.content; state.lastTitle = label;
                out.innerHTML = md(r.content) + saveBtn();
            } else if (type === 'timeline') {
                out.innerHTML = `<div class="nb-tl">${r.content.map(e => `<div><strong>${esc(e.when)}</strong><br>${esc(e.event)}</div>`).join('')}</div>`;
            } else if (type === 'mindmap') {
                out.innerHTML = `<div class="nb-tree"><details open><summary><strong>${esc(r.content.title)}</strong></summary>${tree(r.content.children)}</details></div>`;
            } else if (type === 'flashcards') {
                state.lastCards = r.content;
                out.innerHTML = r.content.map(c => `<div class="nb-card"><strong>${esc(c.question)}</strong><br>${esc(c.answer)}${c.example ? `<br><span class="nb-muted">${esc(c.example)}</span>` : ''}</div>`).join('') +
                    `<button class="btn btn-primary" data-act="save-cards"><i class="fas fa-save"></i> Lưu vào Thư viện thẻ</button>`;
            } else if (type === 'quiz') {
                state.quiz = { items: r.content, score: 0, done: 0 };
                out.innerHTML = quizHtml();
            } else if (type === 'audio') {
                state.audio = r.content;
                out.innerHTML = `<div class="nb-row" style="margin-bottom:var(--space-sm)"><button class="btn btn-primary" data-act="audio-play"><i class="fas fa-play"></i> Phát</button>
                    <button class="btn btn-secondary" data-act="audio-stop"><i class="fas fa-stop"></i> Dừng</button></div>` +
                    r.content.map((l, i) => `<div class="nb-audio-line" id="nb-al-${i}"><strong>${l.speaker === 'B' ? 'B' : 'A'}:</strong> ${esc(l.text)}</div>`).join('') +
                    `<p class="nb-muted">Dùng giọng đọc có sẵn của trình duyệt (Web Speech).</p>`;
            }
        } catch (e) {
            if (version !== workspaceVersion || state.nb?.id !== notebookId) return;
            Object.assign(state, previous);
            status.hidden = false;
            status.className = 'nb-ai-error';
            status.innerHTML = aiError(e) + `<button class="btn btn-secondary" data-act="retry-generate" data-type="${esc(type)}">Thử lại</button>`;
        } finally {
            if (version === workspaceVersion) {
                studioPending = false;
                pane.querySelectorAll('[data-gen], [data-act="retry-generate"]').forEach(button => busy(button, false));
            }
        }
    }
    const saveBtn = () => `<button class="nb-icon-btn" data-act="save-text"><i class="fas fa-bookmark"></i> Lưu ghi chú</button>`;
    function tree(nodes) {
        if (!nodes || !nodes.length) return '';
        return '<ul>' + nodes.map(n => n.children && n.children.length
            ? `<li><details open><summary>${esc(n.title)}</summary>${tree(n.children)}</details></li>` : `<li>${esc(n.title)}</li>`).join('') + '</ul>';
    }
    function quizHtml() {
        const q = state.quiz;
        return q.items.map((it, i) => `<div class="nb-card" data-qi="${i}"><strong>${i + 1}. ${esc(it.question)}</strong>` +
            it.options.map((o, j) => `<button class="nb-opt" data-act="quiz-pick" data-q="${i}" data-o="${j}">${esc(o)}</button>`).join('') +
            `<div class="nb-muted" data-exp="${i}"></div></div>`).join('') + `<div id="nb-quiz-score" class="nb-muted"></div>`;
    }
    function pickAnswer(btn) {
        const i = +btn.dataset.q, j = +btn.dataset.o, it = state.quiz.items[i];
        const card = btn.closest('.nb-card'); if (card.dataset.done) return; card.dataset.done = '1';
        card.querySelectorAll('.nb-opt').forEach((b, k) => { b.disabled = true; if (k === it.correctIndex) b.classList.add('ok'); else if (k === j) b.classList.add('bad'); });
        card.querySelector('[data-exp]').textContent = it.explanation || '';
        state.quiz.done++; if (j === it.correctIndex) state.quiz.score++;
        if (state.quiz.done === state.quiz.items.length) $('#nb-quiz-score').innerHTML = `<strong>Kết quả: ${state.quiz.score}/${state.quiz.items.length}</strong>`;
    }

    // ---------- Audio overview ----------
    function stopAudio() { if ('speechSynthesis' in window) speechSynthesis.cancel(); document.querySelectorAll('.nb-audio-line.on').forEach(e => e.classList.remove('on')); }
    function playAudio() {
        if (!('speechSynthesis' in window)) return alert('Trình duyệt không hỗ trợ đọc giọng nói.');
        stopAudio();
        const vi = speechSynthesis.getVoices().filter(v => v.lang.toLowerCase().startsWith('vi'));
        (state.audio || []).forEach((l, i) => {
            const u = new SpeechSynthesisUtterance(l.text);
            u.lang = 'vi-VN'; u.rate = 1;
            const b = l.speaker === 'B';
            if (vi.length) u.voice = vi[b && vi.length > 1 ? 1 : 0];
            u.pitch = b ? 1.3 : 0.9;
            u.onstart = () => { document.querySelectorAll('.nb-audio-line.on').forEach(e => e.classList.remove('on')); const el = document.getElementById('nb-al-' + i); if (el) { el.classList.add('on'); el.scrollIntoView({ block: 'nearest' }); } };
            speechSynthesis.speak(u);
        });
    }

    // ---------- sự kiện (ủy quyền) ----------
    pane.addEventListener('click', async (e) => {
        const gen = e.target.closest('[data-gen]');
        const el = e.target.closest('[data-act]');
        if (!gen && !el) return;
        if (!ensureLoggedIn()) return;
        if (gen) return generate(gen.dataset.gen, gen);
        const act = el.dataset.act;
        if (act !== 'nb-new' && !state.nb) return;
        if (act === 'retry-chat') return sendQuestion(el.dataset.q, el);
        if (act === 'retry-generate') return generate(el.dataset.type, pane.querySelector(`[data-gen="${el.dataset.type}"]`));
        if (act === 'nb-new') {
            const t = prompt('Tên sổ tay mới:'); if (!t || !t.trim()) return;
            return run(el, async () => { const n = await api.nbCreate(t.trim()); state.notebooks.unshift(n); await loadNotebook(n.id); renderMain(); });
        }
        if (act === 'nb-del') {
            if (!state.nb || !confirm(`Xóa sổ tay "${state.nb.title}" và toàn bộ nguồn, ghi chú?`)) return;
            return run(el, async () => { await api.nbDelete(state.nb.id); state.notebooks = state.notebooks.filter(n => n.id !== state.nb.id); state.nb = null; await start(); });
        }
        if (act === 'add-text') {
            const title = $('#nb-txt-title').value.trim(), text = $('#nb-txt-body').value.trim();
            if (text.length < 20) return alert('Nội dung quá ngắn (tối thiểu 20 ký tự).');
            return run(el, async () => { await api.nbAddText(state.nb.id, title, text); $('#nb-txt-body').value = ''; $('#nb-txt-title').value = ''; await refreshSources(); });
        }
        if (act === 'add-file') {
            const f = $('#nb-file').files[0];
            if (!f) return alert('Hãy chọn một file.');
            if (!/\.(pdf|txt|md|csv)$/i.test(f.name)) return alert('Chỉ hỗ trợ PDF, TXT, MD, CSV.');
            if (f.size > MAX_FILE) return alert('File tối đa 10MB.');
            return run(el, async () => { await api.nbAddFile(state.nb.id, f); $('#nb-file').value = ''; await refreshSources(); });
        }
        if (act === 'add-url') {
            const u = $('#nb-url').value.trim();
            if (!/^https?:\/\/\S+\.\S+/i.test(u)) return showUrlFeedback('URL phải bắt đầu bằng http:// hoặc https://', true);
            return run(el, async () => { await api.nbAddUrl(state.nb.id, u); $('#nb-url').value = ''; await refreshSources(); showUrlFeedback('Đã thêm nguồn từ URL.'); });
        }
        if (act === 'src-del') return run(el, async () => { await api.nbDeleteSource(state.nb.id, el.dataset.id); await refreshSources(); });
        if (act === 'send') return sendQuestion($('#nb-q').value, el);
        if (act === 'suggest') {
            if (!state.sources.length) return alert('Hãy thêm ít nhất một nguồn trước.');
            return run(el, async () => {
                const qs = await api.nbSuggestions(state.nb.id);
                $('#nb-suggest').innerHTML = qs.map(q => `<button class="nb-chip" title="${esc(q)}" data-act="ask" data-q="${esc(q)}">${esc(q)}</button>`).join('');
            });
        }
        if (act === 'ask') return sendQuestion(el.dataset.q, el);
        if (act === 'save-answer') {
            const m = el.closest('.nb-msg');
            return run(el, async () => { await api.nbAddNote(state.nb.id, m.dataset.q.slice(0, 80), m.dataset.raw); await refreshNotes(); });
        }
        if (act === 'save-text') return run(el, async () => { await api.nbAddNote(state.nb.id, state.lastTitle, state.lastText); await refreshNotes(); });
        if (act === 'save-cards') return run(el, async () => {
            const r = await api.nbSaveCards(state.nb.id, state.lastCards);
            window.dispatchEvent(new Event('ms-library-changed'));
            alert(`Đã lưu ${r.saved} thẻ vào Thư viện (chủ đề "${state.nb.title}").`);
        });
        if (act === 'quiz-pick') return pickAnswer(el);
        if (act === 'audio-play') return playAudio();
        if (act === 'audio-stop') return stopAudio();
        if (act === 'note-open') { const n = state.notes.find(x => x.id === +el.dataset.id); if (n) $('#nb-out').innerHTML = `<h4>${esc(n.title)}</h4>` + md(n.content); }
        if (act === 'note-del') return run(el, async () => { await api.nbDeleteNote(state.nb.id, el.dataset.id); await refreshNotes(); });
    });

    pane.addEventListener('change', (e) => {
        if (e.target.id === 'nb-select' && e.target.value && ensureLoggedIn()) run(null, async () => { stopAudio(); await loadNotebook(e.target.value); renderMain(); });
    });
pane.addEventListener('keydown', (e) => {
        if (e.target.id === 'nb-q' && e.key === 'Enter') { e.preventDefault(); sendQuestion(e.target.value, pane.querySelector('[data-act="send"]')); }
    });

    // ---------- auth change listener ----------
    renderMain();
    auth.onAuthChange((user) => {
        resetWorkspace();
        if (user) {
            return start();
        }
    });
})();
