// Sổ tay AI - các tính năng học tập kiểu NotebookLM:
// nguồn tài liệu (dán/PDF/TXT/URL), hỏi đáp có trích dẫn, tóm tắt, study guide, FAQ,
// timeline, mind map, flashcard, trắc nghiệm, audio overview, ghi chú.
(function () {
    const pane = document.getElementById('tab-content-notebook');
    const api = window.MindSprintApi;
    if (!pane || !api) return;

    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const $ = (sel) => pane.querySelector(sel);

    const state = { notebooks: [], nb: null, sources: [], notes: [], history: [], quiz: null, audio: null, lastText: null, lastTitle: '', lastCards: null };
    const MAX_FILE = 10 * 1024 * 1024;

    // ---------- helpers ----------
    const srcTitle = (n) => (state.sources[Number(n) - 1] || {}).title || 'Nguồn ' + n;

    function inline(t) {
        return t.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
            .replace(/\[(\d{1,2})\]/g, (m, n) => `<sup class="nb-cite" title="${esc(srcTitle(n))}">${n}</sup>`);
    }
    function md(src) {
        const out = []; let inList = false;
        for (const raw of esc(src).split('\n')) {
            let m;
            if ((m = raw.match(/^\s*[-*]\s+(.*)/))) { if (!inList) { out.push('<ul>'); inList = true; } out.push('<li>' + inline(m[1]) + '</li>'); continue; }
            if (inList) { out.push('</ul>'); inList = false; }
            if ((m = raw.match(/^(#{1,4})\s+(.*)/))) { const l = m[1].length + 2; out.push(`<h${l}>${inline(m[2])}</h${l}>`); }
            else if (raw.trim()) out.push('<p>' + inline(raw) + '</p>');
        }
        if (inList) out.push('</ul>');
        return out.join('');
    }
    function busy(btn, on) { if (btn) { btn.disabled = on; btn.style.opacity = on ? 0.6 : 1; } }
    async function run(btn, fn) {
        busy(btn, true);
        try { return await fn(); } catch (e) { alert(e.message || 'Có lỗi xảy ra'); } finally { busy(btn, false); }
    }

    // ---------- render: đăng nhập ----------
    let authMode = 'login';
    function renderAuth(err = '') {
        pane.innerHTML = `
        <header class="main-header"><div class="header-info"><h1>Sổ tay AI</h1>
            <p>Đăng nhập để tạo sổ tay, thêm tài liệu và hỏi đáp cùng AI.</p></div></header>
        <div class="nb-auth glass-panel">
            <h3>${authMode === 'login' ? 'Đăng nhập' : 'Tạo tài khoản'}</h3>
            ${authMode === 'register' ? '<input id="nb-name" type="text" placeholder="Tên hiển thị" maxlength="60">' : ''}
            <input id="nb-email" type="email" placeholder="Email" autocomplete="email">
            <input id="nb-pass" type="password" placeholder="Mật khẩu (tối thiểu 6 ký tự)" autocomplete="${authMode === 'login' ? 'current-password' : 'new-password'}">
            <div class="nb-err" id="nb-auth-err">${esc(err)}</div>
            <button class="btn btn-primary" data-act="auth-submit">${authMode === 'login' ? 'Đăng nhập' : 'Đăng ký'}</button>
            <a href="#" data-act="auth-toggle" style="font-size:.85rem;color:var(--primary-color)">${authMode === 'login' ? 'Chưa có tài khoản? Đăng ký' : 'Đã có tài khoản? Đăng nhập'}</a>
        </div>`;
    }

    async function submitAuth(btn) {
        const email = $('#nb-email').value.trim(), pass = $('#nb-pass').value, name = ($('#nb-name') || {}).value?.trim();
        if (!/^\S+@\S+\.\S+$/.test(email)) return renderAuth('Email không hợp lệ.');
        if (pass.length < 6) return renderAuth('Mật khẩu tối thiểu 6 ký tự.');
        if (authMode === 'register' && !name) return renderAuth('Vui lòng nhập tên hiển thị.');
        busy(btn, true);
        try {
            if (authMode === 'login') await api.login(email, pass); else await api.register(email, pass, name);
            await start();
        } catch (e) { renderAuth(e.message); }
    }

    // ---------- render: giao diện chính ----------
    function renderMain() {
        pane.innerHTML = `
        <header class="main-header">
            <div class="header-info"><h1>Sổ tay AI</h1><p>Thêm tài liệu, hỏi đáp có trích dẫn và tạo tài liệu ôn tập từ chính nguồn của bạn.</p></div>
            <div class="header-actions"><button class="btn btn-secondary" data-act="logout"><i class="fas fa-sign-out-alt"></i> Đăng xuất</button></div>
        </header>
        <div class="nb-layout">
            <section class="nb-col nb-sources glass-panel">
                <h3><i class="fas fa-folder-open"></i> Sổ tay & Nguồn</h3>
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
                    <input type="url" id="nb-url" placeholder="https://...">
                    <button class="btn btn-secondary" data-act="add-url">Thêm</button></details>
            </section>

            <section class="nb-col nb-chat glass-panel">
                <h3><i class="fas fa-comments"></i> Hỏi đáp theo nguồn</h3>
                <div class="nb-messages" id="nb-messages"><div class="nb-muted">Thêm nguồn ở bên trái, rồi đặt câu hỏi. Câu trả lời chỉ dựa trên tài liệu của bạn và có số trích dẫn <span class="nb-cite">1</span>.</div></div>
                <div class="nb-suggest" id="nb-suggest"></div>
                <div class="nb-row">
                    <input class="nb-input" id="nb-q" type="text" placeholder="Hỏi về các nguồn..." maxlength="1000">
                    <button class="btn btn-primary" data-act="send"><i class="fas fa-paper-plane"></i></button>
                </div>
                <button class="btn btn-secondary" data-act="suggest" style="align-self:flex-start"><i class="fas fa-lightbulb"></i> Gợi ý câu hỏi</button>
            </section>

            <section class="nb-col nb-studio glass-panel">
                <h3><i class="fas fa-magic"></i> Studio</h3>
                <div class="nb-studio-grid">
                    <button data-gen="summary"><i class="fas fa-file-alt"></i> Tóm tắt</button>
                    <button data-gen="studyguide"><i class="fas fa-graduation-cap"></i> Study guide</button>
                    <button data-gen="faq"><i class="fas fa-question-circle"></i> FAQ</button>
                    <button data-gen="timeline"><i class="fas fa-stream"></i> Timeline</button>
                    <button data-gen="mindmap"><i class="fas fa-project-diagram"></i> Mind map</button>
                    <button data-gen="flashcards"><i class="fas fa-layer-group"></i> Flashcard</button>
                    <button data-gen="quiz"><i class="fas fa-edit"></i> Trắc nghiệm</button>
                    <button data-gen="audio"><i class="fas fa-podcast"></i> Audio overview</button>
                </div>
                <input class="nb-input" id="nb-focus" type="text" placeholder="Trọng tâm (tuỳ chọn), vd: chương 3" maxlength="200">
                <div class="nb-out" id="nb-out"><div class="nb-muted">Kết quả sẽ hiển thị ở đây.</div></div>
                <h3><i class="fas fa-sticky-note"></i> Ghi chú</h3>
                <div id="nb-notes"></div>
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
    async function loadNotebook(id) {
        state.nb = state.notebooks.find(n => n.id === Number(id)) || null;
        state.history = [];
        if (!state.nb) { state.sources = []; state.notes = []; return; }
        [state.sources, state.notes] = await Promise.all([api.nbSources(state.nb.id), api.nbNotes(state.nb.id)]);
    }
    async function start() {
        try {
            state.notebooks = await api.nbList();
            if (!state.notebooks.length) state.notebooks = [await api.nbCreate('Sổ tay đầu tiên')];
            await loadNotebook(state.nb ? state.nb.id : state.notebooks[0].id);
            renderMain();
        } catch (e) { renderAuth(e.message); }
    }
    async function refreshSources() { state.sources = await api.nbSources(state.nb.id); renderSources(); }
    async function refreshNotes() { state.notes = await api.nbNotes(state.nb.id); renderNotes(); }

    // ---------- chat ----------
    function addMsg(role, html) {
        const box = $('#nb-messages');
        if (box.querySelector('.nb-muted') && box.children.length === 1) box.innerHTML = '';
        const d = document.createElement('div'); d.className = 'nb-msg ' + (role === 'user' ? 'user' : 'bot'); d.innerHTML = html;
        box.appendChild(d); box.scrollTop = box.scrollHeight; return d;
    }
    async function sendQuestion(q, btn) {
        q = (q || '').trim(); if (!q) return;
        if (!state.sources.length) return alert('Hãy thêm ít nhất một nguồn trước.');
        $('#nb-q').value = '';
        addMsg('user', esc(q));
        const wait = addMsg('bot', '<span class="nb-muted"><i class="fas fa-spinner fa-spin"></i> Đang đọc nguồn...</span>');
        busy(btn, true);
        try {
            const r = await api.nbChat(state.nb.id, q, state.history);
            state.history.push({ role: 'user', text: q }, { role: 'assistant', text: r.answer });
            const quotes = (r.citations || []).filter(c => c.quote).map(c => `<div><span class="nb-cite">${c.source}</span> <strong>${esc(srcTitle(c.source))}</strong>: “${esc(c.quote)}”</div>`).join('');
            wait.innerHTML = md(r.answer) + (quotes ? `<div class="nb-quotes">${quotes}</div>` : '') +
                `<button class="nb-icon-btn" data-act="save-answer" style="margin-top:.5rem"><i class="fas fa-bookmark"></i> Lưu ghi chú</button>`;
            wait.dataset.raw = r.answer; wait.dataset.q = q;
        } catch (e) { wait.innerHTML = `<span style="color:#ef4444">${esc(e.message)}</span>`; }
        finally { busy(btn, false); }
    }

    // ---------- Studio ----------
    async function generate(type, btn) {
        if (!state.sources.length) return alert('Hãy thêm ít nhất một nguồn trước.');
        stopAudio();
        const out = $('#nb-out');
        out.innerHTML = '<span class="nb-muted"><i class="fas fa-spinner fa-spin"></i> AI đang tạo...</span>';
        state.lastText = null; state.lastCards = null;
        busy(btn, true);
        try {
            const r = await api.nbGenerate(state.nb.id, type, $('#nb-focus').value.trim(), 10);
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
                out.innerHTML = `<div class="nb-row" style="margin-bottom:.5rem"><button class="btn btn-primary" data-act="audio-play"><i class="fas fa-play"></i> Phát</button>
                    <button class="btn btn-secondary" data-act="audio-stop"><i class="fas fa-stop"></i> Dừng</button></div>` +
                    r.content.map((l, i) => `<div class="nb-audio-line" id="nb-al-${i}"><strong>${l.speaker === 'B' ? 'B' : 'A'}:</strong> ${esc(l.text)}</div>`).join('') +
                    `<p class="nb-muted">Dùng giọng đọc có sẵn của trình duyệt (Web Speech).</p>`;
            }
        } catch (e) { out.innerHTML = `<span style="color:#ef4444">${esc(e.message)}</span>`; }
        finally { busy(btn, false); }
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
        if (gen) return generate(gen.dataset.gen, gen);
        const el = e.target.closest('[data-act]'); if (!el) return;
        const act = el.dataset.act;
        if (act === 'auth-toggle') { e.preventDefault(); authMode = authMode === 'login' ? 'register' : 'login'; return renderAuth(); }
        if (act === 'auth-submit') return submitAuth(el);
        if (act === 'logout') { stopAudio(); api.logout(); state.nb = null; return renderAuth(); }
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
            if (!/^https?:\/\/\S+\.\S+/i.test(u)) return alert('URL phải bắt đầu bằng http:// hoặc https://');
            return run(el, async () => { await api.nbAddUrl(state.nb.id, u); $('#nb-url').value = ''; await refreshSources(); });
        }
        if (act === 'src-del') return run(el, async () => { await api.nbDeleteSource(state.nb.id, el.dataset.id); await refreshSources(); });
        if (act === 'send') return sendQuestion($('#nb-q').value, el);
        if (act === 'suggest') {
            if (!state.sources.length) return alert('Hãy thêm ít nhất một nguồn trước.');
            return run(el, async () => {
                const qs = await api.nbSuggestions(state.nb.id);
                $('#nb-suggest').innerHTML = qs.map(q => `<button class="nb-chip" data-act="ask" data-q="${esc(q)}">${esc(q)}</button>`).join('');
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
            alert(`Đã lưu ${r.saved} thẻ vào Thư viện (chủ đề "${state.nb.title}"). Tải lại trang để thấy thẻ mới.`);
        });
        if (act === 'quiz-pick') return pickAnswer(el);
        if (act === 'audio-play') return playAudio();
        if (act === 'audio-stop') return stopAudio();
        if (act === 'note-open') { const n = state.notes.find(x => x.id === +el.dataset.id); if (n) $('#nb-out').innerHTML = `<h4>${esc(n.title)}</h4>` + md(n.content); }
        if (act === 'note-del') return run(el, async () => { await api.nbDeleteNote(state.nb.id, el.dataset.id); await refreshNotes(); });
    });

    pane.addEventListener('change', (e) => {
        if (e.target.id === 'nb-select') run(null, async () => { stopAudio(); await loadNotebook(e.target.value); renderMain(); });
    });
    pane.addEventListener('keydown', (e) => {
        if (e.target.id === 'nb-q' && e.key === 'Enter') { e.preventDefault(); sendQuestion(e.target.value, pane.querySelector('[data-act="send"]')); }
    });

    // ---------- khởi động ----------
    if (api.isLoggedIn()) start(); else renderAuth();
})();
