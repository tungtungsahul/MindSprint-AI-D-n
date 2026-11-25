// Sổ tay AI — tương đương frontend/notebook.js + voice.js của nhóm: chế độ Theo nguồn / Gia sư, hỏi đáp có trích dẫn (lưu lịch sử
// từng sổ trên trình duyệt này), gợi ý câu hỏi, Studio (tóm tắt, hướng dẫn ôn, hỏi đáp thường gặp, dòng thời gian, sơ đồ tư duy,
// nghe tổng quan), Gia sư (giải bài, kiểm tra bài làm, tự luyện, lập trình), nói để nhập và nghe đọc. Nội dung hiện bằng
// MindSprintText.markdown (Markdown + công thức KaTeX như nhóm). Không có JS thì các form thường vẫn chạy.
(() => {
    document.querySelectorAll('[data-autosubmit]').forEach(el => el.addEventListener('change', () => el.form.requestSubmit()));
    const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    const thread = document.getElementById('nb-thread');
    if (!thread || !tokenInput) return;
    const notebookId = thread.dataset.notebook;
    const md = text => window.MindSprintText ? window.MindSprintText.markdown(text) : escapeHtml(text).replace(/\n/g, '<br>');
    const ESCAPES = { 38: '&amp;', 60: '&lt;', 62: '&gt;', 34: '&quot;', 39: '&#39;' }; // mã ký tự: & < > dấu nháy kép, dấu nháy đơn
    const escapeHtml = s => Array.from(String(s ?? ''), ch => ESCAPES[ch.charCodeAt(0)] ?? ch).join('');
    const store = { get(k, d) { try { return JSON.parse(localStorage.getItem(k)) ?? d; } catch { return d; } }, set(k, v) { try { localStorage.setItem(k, JSON.stringify(v)); } catch { } } };

    // POST form-urlencoded hoặc JSON, nhận JSON; lỗi nghiệp vụ trả {error} (BusinessExceptionFilter).
    const post = async (url, data, json = false) => {
        const headers = { 'Accept': 'application/json', 'RequestVerificationToken': tokenInput.value };
        if (json) headers['Content-Type'] = 'application/json';
        const res = await fetch(url, { method: 'POST', headers, body: json ? JSON.stringify(data) : new URLSearchParams(data) }).catch(() => null);
        const body = res ? await res.json().catch(() => null) : null;
        if (!res?.ok) throw new Error(body?.error ?? 'Chưa xử lý được yêu cầu. Hãy thử lại.');
        return body;
    };
    const busy = (button, on) => { if (!button) return; button.disabled = on; button.classList.toggle('is-busy', on); };
    const errorBox = (container, message) => {
        const p = document.createElement('p'); p.className = 'nb-error'; p.setAttribute('role', 'alert'); p.textContent = message; container.append(p);
    };
    const saveNoteForm = (title, content) => {
        const form = document.createElement('form');
        form.method = 'post'; form.action = `/Notebooks/AddNote/${notebookId}`;
        form.innerHTML = `<input type="hidden" name="__RequestVerificationToken" value="${escapeHtml(tokenInput.value)}"><input type="hidden" name="title"><input type="hidden" name="content"><button class="btn btn-sm btn-primary">Lưu vào ghi chú</button>`;
        form.elements.title.value = title.slice(0, 200); form.elements.content.value = content.slice(0, 20000);
        return form;
    };

    // ---------- Chế độ ----------
    document.querySelectorAll('.nb-mode-btn').forEach(btn => btn.addEventListener('click', () => {
        stopSpeaking();
        document.querySelectorAll('.nb-mode-btn').forEach(b => { const on = b === btn; b.classList.toggle('is-active', on); b.setAttribute('aria-checked', String(on)); });
        document.querySelectorAll('.nb-pane').forEach(p => { p.hidden = p.dataset.pane !== btn.dataset.mode; });
    }));

    // ---------- Nghe / Dừng câu trả lời (voice.js của nhóm, FR-16) ----------
    // Chỉ đọc khi người dùng bấm; một giọng đọc tại một lúc; đổi chế độ hoặc rời trang (đổi sổ tay, đăng xuất) thì dừng.
    const canSpeak = 'speechSynthesis' in window;
    const stopSpeaking = () => {
        if (canSpeak) speechSynthesis.cancel();
        document.querySelectorAll('[data-speaking]').forEach(b => { b.removeAttribute('data-speaking'); b.querySelector('span').textContent = 'Nghe'; });
    };
    const speakButton = el => {
        const b = document.createElement('button');
        b.type = 'button'; b.className = 'btn btn-sm btn-secondary'; b.hidden = !canSpeak;
        b.innerHTML = '<svg class="icon" aria-hidden="true"><use href="#i-play"></use></svg><span>Nghe</span>';
        b.addEventListener('click', () => {
            const was = b.hasAttribute('data-speaking');
            stopSpeaking();
            if (was) return;
            const say = new SpeechSynthesisUtterance(el.textContent.trim());
            say.lang = 'vi-VN';
            const voice = speechSynthesis.getVoices().find(v => v.lang?.toLowerCase().startsWith('vi'));
            if (voice) say.voice = voice;
            say.onend = say.onerror = () => { if (b.hasAttribute('data-speaking')) stopSpeaking(); };
            b.setAttribute('data-speaking', ''); b.querySelector('span').textContent = 'Dừng đọc';
            speechSynthesis.speak(say);
        });
        return b;
    };
    window.addEventListener('pagehide', stopSpeaking);

    // ---------- Hỏi đáp theo nguồn (lịch sử lưu theo sổ) ----------
    const historyKey = `mindsprint-nb-thread-${notebookId}`;
    const clearBtn = document.querySelector('[data-clear-thread]');
    const renderTurn = (turn, saveButton = true) => {
        document.getElementById('nb-hero')?.remove();
        const q = document.createElement('div'); q.className = 'nb-q'; q.innerHTML = `<b>Bạn hỏi</b><p></p>`; q.querySelector('p').textContent = turn.q;
        const a = document.createElement('div'); a.className = 'nb-a';
        a.innerHTML = `<b>Sổ tay AI</b><div class="nb-md">${md(turn.a)}</div>` +
            (turn.cites?.length ? `<ol class="nb-cites">${turn.cites.map(c => `<li>“${escapeHtml(c)}”</li>`).join('')}</ol>` : '');
        const actions = document.createElement('div'); actions.className = 'nb-a-actions';
        actions.append(speakButton(a.querySelector('.nb-md')));
        if (saveButton) actions.append(saveNoteForm(turn.q, turn.a));
        a.append(actions);
        thread.append(q, a);
        if (clearBtn) clearBtn.hidden = false;
    };
    const history = store.get(historyKey, []);
    if (!thread.querySelector('.nb-a')) history.forEach(t => renderTurn(t));
    thread.querySelectorAll('[data-markdown]').forEach(el => { el.innerHTML = md(el.textContent); });
    // Câu trả lời do máy chủ dựng (gửi form khi không có JS) cũng có nút Nghe.
    document.querySelector('#nb-answer .nb-a-actions')?.prepend(speakButton(document.querySelector('#nb-answer .nb-md')));
    clearBtn?.addEventListener('click', () => { store.set(historyKey, []); location.reload(); });

    const askForm = document.getElementById('nb-ask');
    const ask = async question => {
        const button = askForm.querySelector('button.btn-primary');
        busy(button, true);
        try {
            const res = await post(askForm.dataset.json, { question });
            const turn = { q: question, a: res.answer, cites: res.citations };
            renderTurn(turn);
            const all = store.get(historyKey, []); all.push(turn); store.set(historyKey, all.slice(-30));
            askForm.elements.question.value = '';
            thread.lastElementChild.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        } catch (e) { errorBox(thread, e.message); }
        finally { busy(button, false); }
    };
    askForm?.addEventListener('submit', e => { e.preventDefault(); const q = askForm.elements.question.value.trim(); if (q) ask(q); });
    document.querySelectorAll('[data-quick-ask]').forEach(btn => btn.addEventListener('click', e => { e.preventDefault(); ask(btn.form.elements.question.value); }));

    // ---------- Gợi ý câu hỏi ----------
    const chips = document.getElementById('nb-suggestions');
    chips?.querySelector('[data-suggest]')?.addEventListener('click', async e => {
        const btn = e.currentTarget; busy(btn, true);
        try {
            const list = await post(chips.dataset.url, {});
            chips.querySelectorAll('.nb-suggestion').forEach(x => x.remove());
            list.forEach(text => {
                const b = document.createElement('button'); b.type = 'button'; b.className = 'nb-chip nb-suggestion'; b.textContent = text;
                b.addEventListener('click', () => { askForm.elements.question.value = text; ask(text); });
                chips.append(b);
            });
            if (!list.length) errorBox(chips, 'Hãy thêm nguồn trước để có gợi ý.');
        } catch (err) { errorBox(chips, err.message); }
        finally { busy(btn, false); }
    });

    // ---------- Studio ----------
    const studio = document.querySelector('.nb-studio');
    const out = document.getElementById('nb-studio-out');
    const titles = { summary: 'Tóm tắt', studyguide: 'Hướng dẫn ôn', faq: 'Hỏi đáp thường gặp', timeline: 'Dòng thời gian', mindmap: 'Sơ đồ tư duy', audio: 'Nghe tổng quan' };
    const mindmapHtml = node => `<li>${escapeHtml(node.title)}${node.children?.length ? `<ul>${node.children.map(mindmapHtml).join('')}</ul>` : ''}</li>`;
    const mindmapText = (node, depth = 0) => `${'  '.repeat(depth)}- ${node.title}\n` + (node.children ?? []).map(c => mindmapText(c, depth + 1)).join('');
    const showStudio = (type, result) => {
        let html = '', text = '';
        if (result.markdown !== undefined) { html = `<div class="nb-md">${md(result.markdown)}</div>`; text = result.markdown; }
        else if (type === 'timeline') {
            html = `<ol class="nb-timeline">${(result.items ?? []).map(i => `<li><b>${escapeHtml(i.when)}</b><span>${escapeHtml(i.event)}</span></li>`).join('')}</ol>`;
            text = (result.items ?? []).map(i => `- **${i.when}**: ${i.event}`).join('\n');
        } else if (type === 'mindmap') { html = `<ul class="nb-mindmap">${mindmapHtml(result)}</ul>`; text = mindmapText(result); }
        else if (type === 'audio') {
            html = `<div class="nb-audio"><button type="button" class="btn btn-sm btn-primary" data-play>Phát</button> <button type="button" class="btn btn-sm btn-secondary" data-stop>Dừng</button>` +
                `<ol>${(result.lines ?? []).map(l => `<li><b>${escapeHtml(l.speaker)}</b> ${escapeHtml(l.text)}</li>`).join('')}</ol></div>`;
            text = (result.lines ?? []).map(l => `**${l.speaker}:** ${l.text}`).join('\n');
        }
        const card = document.createElement('article'); card.className = 'nb-out';
        card.innerHTML = `<div class="nb-out-head"><h3>${titles[type]}</h3></div>${html}`;
        card.querySelector('.nb-out-head').append(saveNoteForm(titles[type], text));
        if (type === 'audio') {
            card.querySelector('[data-play]').addEventListener('click', () => playDialogue(result.lines ?? []));
            card.querySelector('[data-stop]').addEventListener('click', () => window.speechSynthesis?.cancel());
            if (!('speechSynthesis' in window)) card.querySelectorAll('[data-play],[data-stop]').forEach(b => b.hidden = true);
        }
        out.prepend(card);
    };
    studio?.querySelectorAll('button[data-gen]').forEach(btn => btn.addEventListener('click', async () => {
        busy(btn, true);
        try { const res = await post(studio.dataset.url, { type: btn.dataset.gen, focus: document.getElementById('nb-focus')?.value ?? '' }); showStudio(res.type, res.result); }
        catch (e) { const box = document.createElement('div'); errorBox(box, e.message); out.prepend(box); }
        finally { busy(btn, false); }
    }));

    // ---------- Studio Flashcard / Trắc nghiệm: làm thử tại chỗ (quizHtml/pickAnswer của nhóm) rồi tùy chọn lưu ----------
    // "Lưu thành bộ học" gửi đúng các dòng bản nháp tới Import/Recheck: máy chủ kiểm tra lại toàn bộ, mở trang xem trước để bấm Lưu.
    const saveSetForm = res => {
        const form = document.createElement('form');
        form.method = 'post'; form.action = studio.dataset.saveUrl;
        const add = (name, value) => { const i = document.createElement('input'); i.type = 'hidden'; i.name = name; i.value = value ?? ''; form.append(i); };
        add('__RequestVerificationToken', tokenInput.value); add('QuizId', '0'); add('FileName', res.fileName); add('NewSetTitle', res.newSetTitle);
        res.rows.forEach((r, i) => {
            add(`Rows[${i}].Sheet`, r.sheet); add(`Rows[${i}].Row`, r.row); add(`Rows[${i}].Include`, r.include ? 'true' : 'false');
            (r.values ?? []).forEach((v, j) => add(`Rows[${i}].Values[${j}]`, v));
        });
        const b = document.createElement('button'); b.className = 'btn btn-sm btn-primary'; b.textContent = 'Lưu thành bộ học'; form.append(b);
        return form;
    };
    const showItems = res => {
        const quiz = res.kind === 'quiz', items = res.items ?? [];
        const card = document.createElement('article'); card.className = 'nb-out';
        card.innerHTML = `<div class="nb-out-head"><h3>${quiz ? 'Trắc nghiệm' : 'Flashcard'} (${items.length})</h3></div>`;
        card.querySelector('.nb-out-head').append(saveSetForm(res));
        if (res.notice) { const n = document.createElement('p'); n.className = 'nb-notice'; n.setAttribute('role', 'status'); n.textContent = res.notice; card.append(n); }
        const list = document.createElement('ol'); list.className = quiz ? 'nb-quiz' : 'nb-flash';
        const score = document.createElement('p'); score.className = 'nb-quiz-score'; score.setAttribute('role', 'status');
        let done = 0, right = 0;
        items.forEach(it => {
            const li = document.createElement('li');
            if (!quiz) {
                li.innerHTML = `<button type="button" class="nb-flash-card"><span class="nb-md">${md(it.front)}</span><span class="nb-md nb-flash-back" hidden>${md(it.back)}</span><small>Nhấp để lật thẻ</small></button>`;
                const b = li.querySelector('button');
                b.addEventListener('click', () => (window.MindSprintFlip ?? ((_, f) => f()))(b, () => {
                    const back = b.querySelector('.nb-flash-back'); back.hidden = !back.hidden; b.classList.toggle('is-flipped', !back.hidden);
                }));
                list.append(li); return;
            }
            li.innerHTML = `<div class="nb-md">${md(it.front)}</div><div class="nb-quiz-opts"></div><div class="nb-quiz-exp" hidden></div>`;
            const opts = li.querySelector('.nb-quiz-opts'), multi = it.correct.length > 1, chosen = new Set();
            const check = () => {
                const ok = chosen.size === it.correct.length && it.correct.every(k => chosen.has(k));
                opts.querySelectorAll('button').forEach(b => {
                    const k = Number(b.dataset.k); b.disabled = true;
                    if (it.correct.includes(k)) b.classList.add('is-ok'); else if (chosen.has(k)) b.classList.add('is-bad');
                });
                const exp = li.querySelector('.nb-quiz-exp');
                exp.innerHTML = `<b>${ok ? 'Đúng' : 'Chưa đúng'}</b>${it.explanation ? ` · ${md(it.explanation)}` : ''}`; exp.hidden = false;
                done++; if (ok) right++;
                if (done === items.length) score.textContent = `Kết quả: ${right}/${items.length} câu đúng`;
            };
            it.options.forEach((o, k) => {
                if (!o) return;
                const b = document.createElement('button'); b.type = 'button'; b.className = 'nb-opt'; b.dataset.k = k; b.textContent = o;
                b.addEventListener('click', () => {
                    if (!multi) { chosen.add(k); check(); return; }
                    chosen.has(k) ? chosen.delete(k) : chosen.add(k); b.classList.toggle('is-picked', chosen.has(k)); b.setAttribute('aria-pressed', String(chosen.has(k)));
                });
                opts.append(b);
            });
            if (multi) {
                const c = document.createElement('button'); c.type = 'button'; c.className = 'btn btn-sm btn-secondary'; c.textContent = 'Kiểm tra (chọn nhiều đáp án)';
                c.addEventListener('click', () => { if (chosen.size) { c.remove(); check(); } });
                li.append(c);
            }
            list.append(li);
        });
        card.append(list);
        if (quiz) card.append(score);
        out.prepend(card);
        card.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    };
    document.querySelectorAll('form[data-items]').forEach(form => form.addEventListener('submit', async e => {
        if (!studio?.dataset.itemsUrl) return; // không có khung Studio → gửi form như cũ (trang xem trước)
        e.preventDefault();
        const btn = form.querySelector('button'); busy(btn, true);
        try { showItems(await post(studio.dataset.itemsUrl, { kind: form.dataset.items })); }
        catch (err) { const box = document.createElement('div'); errorBox(box, err.message); out.prepend(box); }
        finally { busy(btn, false); }
    }));

    // Nghe tổng quan: đọc kịch bản hai người dẫn bằng giọng đọc của trình duyệt (A giọng thường, B cao hơn) như audio overview của nhóm.
    const viVoice = () => window.speechSynthesis?.getVoices().find(v => v.lang?.toLowerCase().startsWith('vi'));
    const playDialogue = lines => {
        if (!('speechSynthesis' in window)) return;
        speechSynthesis.cancel();
        lines.forEach(l => {
            const u = new SpeechSynthesisUtterance(l.text); u.lang = 'vi-VN'; const v = viVoice(); if (v) u.voice = v;
            u.pitch = l.speaker === 'B' ? 1.25 : 0.95; u.rate = 1;
            speechSynthesis.speak(u);
        });
    };

    // ---------- Gia sư ----------
    const tutor = document.getElementById('nb-tutor');
    const tutorThread = document.getElementById('nb-tutor-thread');
    let tutorHistory = [];
    const syncTutor = () => {
        const action = tutor.elements.action.value;
        tutor.querySelectorAll('[data-when]').forEach(el => { el.hidden = el.dataset.when !== action; });
        tutor.elements.question.required = action !== 'code';
    };
    tutor?.addEventListener('change', e => { if (e.target.name === 'action') syncTutor(); });
    tutor && syncTutor();
    tutor?.querySelector('[data-tutor-new]')?.addEventListener('click', () => { tutorHistory = []; tutorThread.replaceChildren(); tutor.reset(); syncTutor(); });
    const statusText = { correct: 'Bài làm đúng', incorrect: 'Có chỗ sai', incomplete: 'Chưa hoàn chỉnh', needs_clarification: 'Cần bổ sung đề' };
    const errorText = { none: '', calculation: 'Lỗi tính toán', reasoning: 'Lỗi phương pháp / lập luận', missing_step: 'Thiếu bước' };
    tutor?.addEventListener('submit', async e => {
        e.preventDefault();
        const f = tutor.elements, action = f.action.value;
        const data = { action, style: f.style.value, question: f.question.value, history: tutorHistory.slice(-12) };
        if (action === 'hint') data.hintLevel = Number(f.hintLevel.value);
        if (action === 'check') data.submission = f.submission.value;
        if (action === 'code') { data.language = f.language.value; data.code = f.code.value; data.error = f.error.value || null; }
        const button = tutor.querySelector('button.btn-primary'); busy(button, true);
        try {
            const r = await post(tutor.dataset.url, data, true);
            const asked = action === 'code' ? (data.question || 'Phân tích đoạn code') : data.question;
            const q = document.createElement('div'); q.className = 'nb-q'; q.innerHTML = '<b>Bạn hỏi</b><p></p>'; q.querySelector('p').textContent = asked;
            const a = document.createElement('div'); a.className = 'nb-a';
            let extra = '';
            if (r.feedback) extra += `<div class="nb-feedback is-${escapeHtml(r.feedback.status)}"><b>${escapeHtml(statusText[r.feedback.status] ?? r.feedback.status)}</b>` +
                `${errorText[r.feedback.errorType] ? ` · ${errorText[r.feedback.errorType]}` : ''}${r.feedback.firstErrorStep ? ` · Bước sai đầu tiên: ${escapeHtml(r.feedback.firstErrorStep)}` : ''}` +
                `<p>${escapeHtml(r.feedback.reason)}</p><p><b>Gợi ý sửa:</b> ${escapeHtml(r.feedback.suggestion)}</p></div>`;
            if (r.programming) {
                const p = r.programming;
                extra += `<div class="nb-code-review"><p><b>Chẩn đoán:</b> ${escapeHtml(p.diagnosis)}${p.location ? ` (${escapeHtml(p.location)})` : ''}</p>` +
                    (p.fixedCode ? `<p><b>Bản sửa:</b></p><pre><code></code></pre>` : '') + `<div class="nb-md">${md(p.explanation)}</div>` +
                    (p.tests?.length ? `<table class="table table-sm"><thead><tr><th>Đầu vào</th><th>Đầu ra dự kiến</th><th>Mục đích</th></tr></thead><tbody>${p.tests.map(t => `<tr><td>${escapeHtml(t.input)}</td><td>${escapeHtml(t.expectedOutput)}</td><td>${escapeHtml(t.purpose)}</td></tr>`).join('')}</tbody></table>` : '') + '</div>';
            }
            a.innerHTML = `<b>Gia sư</b><div class="nb-md">${md(r.answer)}</div>${extra}`;
            if (r.programming?.fixedCode) a.querySelector('pre code').textContent = r.programming.fixedCode;
            const actions = document.createElement('div'); actions.className = 'nb-a-actions'; actions.append(speakButton(a.querySelector('.nb-md')), saveNoteForm(`Gia sư: ${asked}`, r.answer)); a.append(actions);
            tutorThread.append(q, a);
            tutorHistory.push({ role: 'user', text: asked.slice(0, 10000) }, { role: 'assistant', text: String(r.answer).slice(0, 10000) });
            f.question.value = ''; if (action === 'check') f.submission.value = '';
        } catch (err) { errorBox(tutorThread, err.message); }
        finally { busy(button, false); }
    });

    // ---------- Nói để nhập (như voice.js của nhóm) ----------
    const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    document.querySelectorAll('[data-voice-input]').forEach(mic => {
        const box = document.getElementById(mic.dataset.target);
        if (!Recognition || !box) return;
        mic.hidden = false;
        let rec = null;
        mic.addEventListener('click', () => {
            if (rec) { rec.stop(); return; }
            rec = new Recognition(); rec.lang = 'vi-VN'; rec.interimResults = true;
            const before = box.value ? box.value.trimEnd() + ' ' : '';
            rec.onresult = e => { box.value = before + [...e.results].map(r => r[0].transcript).join(''); };
            rec.onend = () => { rec = null; mic.setAttribute('aria-pressed', 'false'); mic.querySelector('span').textContent = 'Nhập bằng giọng nói'; box.focus(); };
            rec.onerror = () => rec?.stop();
            mic.setAttribute('aria-pressed', 'true'); mic.querySelector('span').textContent = 'Đang nghe… bấm để dừng';
            rec.start();
        });
    });
})();
