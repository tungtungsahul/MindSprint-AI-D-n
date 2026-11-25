// Mọi việc chấm điểm ở máy chủ; trình duyệt chỉ gửi lựa chọn và hiện phản hồi được phép.
(() => {
    const root = document.getElementById('play');
    if (!root) return;
    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
    const sections = [...root.querySelectorAll('.question')];
    const SINGLE = '1';
    const test = root.dataset.mode === '2';
    const mastery = !!root.dataset.mastery;
    const survey = s => ['11', '12', '13'].includes(s.dataset.type);
    const canRetry = s => s.dataset.canRetry === '1';
    let current = 0;
    let activeAnswers = 0;
    let openQueue = Promise.resolve(), timerCheck = false;
    const locked = s => s.dataset.locked === '1';

    const ids = text => (text || '').split(',').filter(Boolean).map(Number);
    const inputs = s => [...s.querySelectorAll('.answer-input')];
    function inputError(section) {
        const values = inputs(section).map(i => i.value.trim());
        if (section.dataset.type === '3') {
            const empty = values.findIndex(v => !v);
            return empty < 0 ? '' : `Chỗ trống ${empty + 1} chưa điền.`;
        }
        if (section.dataset.type === '4') {
            const value = (values[0] || '').replaceAll('−', '-');
            if (!value) return 'Chưa nhập số';
            if (/[^0-9\s.,+\-]/.test(value)) return 'Chỉ nhập số';
            if (!/^[+-]?(?:[0-9]+(?:[.,][0-9]*)?|[.,][0-9]+)$/.test(value)) return 'Số không hợp lệ';
        }
        if (section.dataset.type === '13') {
            if (!values[0]) return 'Hãy nhập ý kiến.';
            if (inputs(section)[0].value.length > 500) return 'Ý kiến tối đa 500 ký tự.';
        }
        return '';
    }
    function payload(section, selected) {
        const body = { attemptQuestionId: Number(section.dataset.id), selected, questionVersion: Number(section.dataset.questionVersion) };
        if (section.dataset.type === '3') body.blanks = inputs(section).map(i => i.value);
        if (['4', '13'].includes(section.dataset.type)) body.value = inputs(section)[0].value;
        return body;
    }
    function enqueueDraft(section, body) {
        section.draftQueue = (section.draftQueue || Promise.resolve()).then(async () => {
            try { await post(root.dataset.draftUrl, body); }
            catch (err) { section.querySelector('.feedback').textContent = err.message; }
        });
    }
    function flushDraft(section) {
        if (!section.draftTimer) return;
        clearTimeout(section.draftTimer);
        section.draftTimer = null;
        enqueueDraft(section, payload(section, []));
    }
    // Trang đã cũ (StaleFormFilter): bấm lại vô ích, chỉ tải lại trang mới gửi được. Khối riêng vì #question-notice hay bị ghi đè.
    function showReload(text) {
        if (document.getElementById('stale-page')) return;
        const box = document.createElement('div');
        box.id = 'stale-page'; box.className = 'alert alert-warning d-flex flex-wrap align-items-center gap-2'; box.setAttribute('role', 'alert');
        const p = document.createElement('span'); p.textContent = `${text} Các câu đã lưu vẫn còn.`;
        const b = document.createElement('button'); b.type = 'button'; b.className = 'btn btn-sm btn-primary'; b.textContent = 'Tải lại trang';
        b.addEventListener('click', () => location.reload());
        box.append(p, b);
        document.getElementById('question-notice').before(box);
        // Không giật con trỏ khỏi ô đang gõ (tài khoản 2 rà); role="alert" đã đọc thông báo cho trình đọc màn hình.
        if (document.activeElement === document.body) b.focus();
    }
    document.addEventListener('learn:stale', e => showReload(e.detail));
    const post = async (url, body, asHtml = false) => {
        if (root.inert) throw new Error('Lượt này không còn nhận thao tác.');
        const res = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'Accept': asHtml ? 'text/html' : 'application/json', 'RequestVerificationToken': token },
            body: body ? JSON.stringify(body) : null
        });
        if (res.ok && asHtml) return res.text();
        const data = await res.json().catch(() => ({}));
        if (data.reload) showReload(data.error);
        if (!res.ok) throw new Error(data.error || 'Không gửi được, hãy thử lại.');
        if (Object.hasOwn(data, 'deadlineAt')) root.dispatchEvent(new CustomEvent('learn:state', { detail: data }));
        return data;
    };

    function lockInputs(section) {
        section.querySelectorAll('.option, .confirm, .answer-input').forEach(el => el.disabled = true);
        section.querySelector('.next').disabled = false;
    }
    function displayClock(section) {
        const clock = section.questionClock;
        const left = clock == null ? null : Math.max(0, Math.ceil(clock.left - (clock.running ? (performance.now() - clock.at) / 1000 : 0)));
        const text = locked(section) ? (test ? 'Đã khóa' : 'Hết giờ') : left == null ? '' : `còn ${Math.floor(left / 60)}:${String(left % 60).padStart(2, '0')}`;
        const badge = section.querySelector('.question-countdown');
        badge.hidden = !text; badge.textContent = text;
        root.querySelectorAll('.nav-clock')[sections.indexOf(section)].textContent = text;
        return left;
    }
    function applyClock(section, data, announce = true) {
        const wasLocked = locked(section);
        section.dataset.locked = data.locked ? '1' : '0';
        section.questionClock = data.secondsLeft == null ? null : { left: data.secondsLeft, at: performance.now(), running: data.running ?? !data.locked };
        displayClock(section);
        if (data.locked) lockInputs(section);
        if (!data.expired || wasLocked || !announce) return;
        questionExpired(section);
    }
    function questionExpired(section) {
        const i = sections.indexOf(section);
        document.getElementById('question-notice').textContent = `Câu ${i + 1} đã hết giờ.`;
        if (!test) {
            // (59) tải phản hồi qua bộ lọc của service, giữ nguyên câu đang xem.
            location.hash = `question-${sections[current].dataset.id}`;
            location.reload();
        } else if (i === current) {
            const next = sections.findIndex((s, k) => k > i && s.dataset.answered !== '1' && !locked(s));
            if (next >= 0) { go(next); sections[next].focus(); }
            else document.getElementById('navigator-title').focus();
        }
    }
    function openClock(section, retry = false) {
        // (59) tuần tự hóa mở câu để phản hồi chậm không làm câu cũ chạy lại.
        const task = openQueue.catch(() => {}).then(async () => {
            const template = document.createElement('template');
            template.innerHTML = await post(root.dataset.openUrl, { attemptQuestionId: Number(section.dataset.id), retry, questionVersion: Number(section.dataset.questionVersion) }, true);
            const body = template.content.firstElementChild;
            const data = {
                secondsLeft: body.dataset.secondsLeft === '' ? null : Number(body.dataset.secondsLeft),
                locked: body.dataset.clockLocked === '1', expired: body.dataset.clockExpired === '1'
            };
            if (section.querySelector('.question-placeholder')) {
                section.replaceChildren(template.content);
                initSection(section);
                if (sections[current] === section) section.querySelector('.card-body').focus();
            }
            if (!test) sections.forEach(s => {
                if (s === section || !s.questionClock) return;
                s.questionClock.left = displayClock(s); s.questionClock.at = performance.now(); s.questionClock.running = false;
            });
            applyClock(section, { ...data, running: !data.locked && (test || section.dataset.answered !== '1' || section.dataset.retrying === '1' || retry) });
            if (sections.some(s => s.dataset.limit)) await syncClocks();
            return data;
        });
        section.openTask = openQueue = task;
        return task;
    }

    // Tô màu câu đã trả lời: chọn đúng nền xanh; chọn sai nền đỏ và đáp án đúng nền xanh.
    function showResult(section) {
        section.dataset.retrying = '0';
        const selected = ids(section.dataset.selected);
        const correct = ids(section.dataset.correct);
        section.querySelectorAll('.option').forEach(btn => {
            const id = Number(btn.dataset.option);
            btn.disabled = true;
            btn.classList.remove('btn-outline-secondary', 'btn-secondary');
            btn.classList.add(survey(section) ? (selected.includes(id) ? 'btn-secondary' : 'btn-outline-secondary') : correct.includes(id) || section.dataset.isCorrect === '1' && selected.includes(id) ? 'btn-success' : selected.includes(id) ? 'btn-danger' : 'btn-outline-secondary');
            btn.setAttribute('aria-pressed', selected.includes(id) ? 'true' : 'false');
            btn.setAttribute('aria-label', btn.textContent + (btn.classList.contains('btn-success') ? ' — Đáp án đúng'
                : btn.classList.contains('btn-danger') ? ' — Bạn chọn, chưa đúng' : selected.includes(id) ? ' — Đã chọn' : ''));
        });
        section.querySelector('.confirm')?.classList.add('d-none');
        section.querySelector('.next').disabled = false;
        const matches = (section.dataset.blankCorrect || '').split(',');
        inputs(section).forEach((input, i) => {
            input.disabled = true;
            const ok = section.dataset.type === '3' ? matches[i] === '1' : section.dataset.isCorrect === '1';
            if (!survey(section)) {
                input.classList.add(ok ? 'is-valid' : 'is-invalid');
                input.setAttribute('aria-label', (input.labels?.[0]?.textContent || 'Câu trả lời') + (ok ? ' — Đúng' : ' — Chưa đúng'));
                input.setAttribute('aria-invalid', String(!ok));
            }
        });
        const fb = section.querySelector('.feedback');
        const ok = section.dataset.isCorrect === '1';
        // Câu điền chữ/số không có dấu ✓ trên lựa chọn: đáp án đúng hiện thành danh sách bên dưới.
        const typed = section.dataset.type === '3' || section.dataset.type === '4';
        fb.textContent = survey(section) ? 'Đã ghi nhận câu trả lời' : ok ? 'Đúng!' : typed ? 'Chưa đúng. Đáp án đúng ở bên dưới.' : 'Chưa đúng. Đáp án đúng có dấu ✓.';
        fb.className = 'feedback mt-3 fw-bold ' + (survey(section) ? 'text-muted' : ok ? 'text-success' : 'text-danger');
        section.querySelector('.retry').classList.toggle('d-none', !canRetry(section));
        section.querySelector('.explanation').classList.remove('d-none');
    }

    function showSelection(section) {
        if (locked(section)) { lockInputs(section); return; }
        inputs(section).forEach(input => {
            input.disabled = false;
            input.classList.remove('is-valid', 'is-invalid');
            input.removeAttribute('aria-label');
            input.removeAttribute('aria-invalid');
        });
        const selected = ids(section.dataset.selected);
        section.querySelectorAll('.option').forEach(btn => {
            const on = selected.includes(Number(btn.dataset.option));
            btn.disabled = false;
            btn.classList.remove('btn-success', 'btn-danger');
            btn.classList.toggle('btn-secondary', on);
            btn.classList.toggle('btn-outline-secondary', !on);
            btn.setAttribute('aria-pressed', String(on));
            btn.setAttribute('aria-label', btn.textContent + (on ? ' — Đã chọn' : ''));
        });
        const confirmButton = section.querySelector('.confirm');
        if (confirmButton) { confirmButton.disabled = false; confirmButton.classList.remove('d-none'); }
        section.validateInput?.();
    }

    async function reopen(section) {
        if (!canRetry(section)) return;
        try { await openClock(section, true); }
        catch (err) { section.querySelector('.feedback').textContent = err.message; return; }
        section.dataset.retrying = '1';
        section.dataset.selected = '';
        delete section.dataset.requestId;
        showSelection(section);
        section.querySelector('.retry').classList.add('d-none');
        // (22B) phản hồi của lần trước vẫn được phép xem khi mở lại câu sai.
        section.querySelector('.feedback').textContent = 'Lần trước chưa đúng. Hãy chọn lại để luyện.';
        section.querySelector('.option')?.focus();
    }

    function refresh() {
        const answered = sections.filter(s => s.dataset.answered === '1');
        const done = answered.length, total = sections.length;
        document.getElementById('progress-bar').style.width = `${(done / total) * 100}%`;
        document.getElementById('progress-text').textContent = `Đã trả lời ${done}/${total}`;
        root.querySelectorAll('.nav-q').forEach((btn, i) => {
            const s = sections[i];
            btn.className = 'btn btn-sm nav-q ' + (s.dataset.answered !== '1' ? 'btn-outline-secondary'
                : test || survey(s) ? 'btn-primary' : s.dataset.isCorrect === '1' ? 'btn-success' : 'btn-danger');
            if (s.dataset.flagged === '1') btn.classList.add('flagged');
            if (i === current) btn.classList.add('current');
            btn.setAttribute('aria-label', `Câu ${i + 1}` + (s.dataset.flagged === '1' ? ', đã đánh dấu' : '')
                + (s.dataset.answered !== '1' ? ', chưa trả lời' : test || survey(s) ? ', đã trả lời' : s.dataset.isCorrect === '1' ? ', đúng' : ', chưa đúng'));
            if (i === current) btn.setAttribute('aria-current', 'step');
            else btn.removeAttribute('aria-current');
        });
        const left = sections.filter(s => s.dataset.answered !== '1' && !(survey(s) && locked(s))).length;
        const required = sections.filter(s => s.dataset.required === '1' && s.dataset.answered !== '1' && !locked(s));
        const remaining = sections.filter(s => !survey(s) && s.dataset.answered !== '1').length;
        const retryable = sections.some(canRetry);
        const reached = root.dataset.masteryReached === '1';
        document.getElementById('finish').disabled = activeAnswers > 0 || required.length > 0
            || (mastery ? left > 0 || (!reached && retryable) : !test && remaining > 0);
        if (test) document.getElementById('finish-form').dataset.confirm = left > 0 ? `Còn ${left} câu chưa trả lời, vẫn nộp?` : 'Nộp bài Kiểm tra?';
        document.getElementById('finish-hint').textContent = required.length > 0
            ? 'Còn câu khảo sát bắt buộc chưa trả lời: câu ' + required.map(s => sections.indexOf(s) + 1).join(', ')
            : mastery && left === 0 && !reached && retryable ? 'Hãy luyện tiếp các câu sai để đạt mốc.'
            : left > 0 ? `Còn ${left} câu chưa trả lời.` : 'Đã trả lời hết, có thể nộp bài.';
        if (mastery) {
            const count = root.dataset.mastered, threshold = root.dataset.mastery, denominator = root.dataset.masteryTotal;
            const label = `Làm chủ ${count}/${denominator} (mốc ${threshold}%)`;
            document.getElementById('mastery-progress').textContent = reached ? `Bạn đã đạt mốc ${threshold}%! ${label}`
                : left === 0 && !retryable ? `Bạn đã dùng hết số lần trả lời, chưa đạt mốc (${count}/${denominator}, mốc ${threshold}%)` : label;
            document.getElementById('mastery-bar').value = Number(count);
            document.getElementById('practice-more').classList.toggle('d-none', !reached || !retryable);
            const group = document.getElementById('wrong-questions');
            group.replaceChildren();
            sections.forEach((s, i) => {
                if (survey(s) || s.dataset.answered !== '1' || s.dataset.isCorrect !== '0') return;
                const button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-sm btn-outline-danger';
                button.textContent = String(i + 1); button.setAttribute('aria-label', `Câu sai ${i + 1}`);
                button.addEventListener('click', () => { go(i); reopen(s); });
                group.appendChild(button);
            });
        }
    }

    function go(index) {
        current = Math.max(0, Math.min(sections.length - 1, index));
        sections.forEach((s, i) => s.classList.toggle('d-none', i !== current));
        const s = sections[current];
        location.hash = `question-${s.dataset.id}`;
        s.querySelector('.flag').textContent = s.dataset.flagged === '1' ? 'Bỏ đánh dấu' : 'Đánh dấu để xem lại';
        lockInputs(s);
        openClock(s).then(() => {
            if (locked(s)) return;
            if (test || s.dataset.answered !== '1' || s.dataset.retrying === '1') showSelection(s);
        }).catch(err => { s.querySelector('.feedback').textContent = err.message; });
        refresh();
    }

    async function submit(section, selected) {
        activeAnswers++;
        refresh();
        section.querySelectorAll('.option, .confirm, .answer-input').forEach(b => b.disabled = true);
        try {
            await section.openTask;
            flushDraft(section);
            await section.draftQueue;
            const body = payload(section, selected);
            const signature = JSON.stringify(body);
            if (section.dataset.requestSelection !== signature) {
                section.dataset.requestSelection = signature;
                delete section.dataset.requestId;
            }
            body.clientRequestId = section.dataset.requestId ||= crypto.randomUUID();
            const r = await post(root.dataset.answerUrl, body);
            Object.assign(section.dataset, { answered: '1', selected: selected.join(',') });
            delete section.dataset.requestId;
            if (test) {
                showSelection(section);
                section.querySelector('.feedback').textContent = 'Đã xác nhận. Bạn có thể đổi đến khi nộp bài.';
            } else {
                if (section.questionClock) { section.questionClock.left = displayClock(section); section.questionClock.running = false; section.questionClock.at = performance.now(); }
                Object.assign(section.dataset, { correct: (r.correctOptionIds || []).join(','), isCorrect: r.isCorrect == null ? '' : r.isCorrect ? '1' : '0', canRetry: r.canRetry ? '1' : '0' });
                if (r.mastery) Object.assign(root.dataset, { mastered: String(r.mastery.mastered), masteryTotal: String(r.mastery.total), masteryReached: r.mastery.reached ? '1' : '0' });
                section.querySelector('.explanation').textContent = r.explanation || '';
                section.dataset.blankCorrect = (r.blankCorrect || []).map(x => x ? '1' : '0').join(',');
                const solutions = section.querySelector('.input-solutions');
                solutions.replaceChildren();
                (r.correctAnswers || []).forEach(answer => {
                    const li = document.createElement('li');
                    li.textContent = answer;
                    solutions.appendChild(li);
                });
                showResult(section);
            }
            refresh();
        } catch (err) {
            section.querySelector('.feedback').textContent = err.message;
            if (!locked(section)) section.querySelectorAll('.option, .confirm, .answer-input').forEach(b => b.disabled = false);
        } finally {
            activeAnswers--;
            refresh();
        }
    }

    function initSection(section) {
        const i = sections.indexOf(section);
        Object.assign(section.dataset, section.querySelector('.question-rules').dataset);
        section.querySelector('.flag').textContent = section.dataset.flagged === '1' ? 'Bỏ đánh dấu' : 'Đánh dấu để xem lại';
        section.questionClock ??= section.dataset.limit ? { left: Number(section.dataset.limit), at: performance.now(), running: false } : null;
        const result = section.querySelector('.result');
        if (result) Object.assign(section.dataset, { correct: result.dataset.correct, isCorrect: result.dataset.isCorrect, blankCorrect: result.dataset.blankCorrect });
        if (test) showSelection(section);
        else if (section.dataset.answered === '1') showResult(section);
        else section.querySelector('.explanation').classList.add('d-none');
        function validateInput() {
            if (locked(section)) { lockInputs(section); return; }
            if (inputs(section).length === 0 || (!test && section.dataset.answered === '1' && section.dataset.retrying !== '1')) return;
            const error = inputError(section);
            section.querySelector('.confirm').disabled = !!error;
            section.querySelector('.next').disabled = !!error;
            section.querySelector('.feedback').textContent = error;
        }
        section.validateInput = validateInput;
        inputs(section).forEach(input => input.addEventListener('input', () => {
            validateInput();
            if (!test) return;
            clearTimeout(section.draftTimer);
            section.draftTimer = setTimeout(() => {
                section.draftTimer = null;
                enqueueDraft(section, payload(section, []));
            }, 800);
        }));
        validateInput();
        if (locked(section)) lockInputs(section);

        section.querySelectorAll('.option').forEach(btn => btn.addEventListener('click', () => {
            if (locked(section)) return;
            if (!test && section.dataset.answered === '1' && section.dataset.retrying !== '1') return;
            if ([SINGLE, '11'].includes(section.dataset.type)) return submit(section, [Number(btn.dataset.option)]); // chọn là chốt
            const on = btn.getAttribute('aria-pressed') !== 'true';
            btn.setAttribute('aria-pressed', on ? 'true' : 'false');
            btn.setAttribute('aria-label', btn.textContent + (on ? ' — Đã chọn' : ''));
            btn.classList.toggle('btn-secondary', on);
            btn.classList.toggle('btn-outline-secondary', !on);
            if (test) {
                const selected = [...section.querySelectorAll('.option[aria-pressed="true"]')].map(b => Number(b.dataset.option));
                section.dataset.selected = selected.join(',');
                // (15b(1)) gửi nháp theo thứ tự thao tác để phản hồi mạng chậm không ghi đè nháp mới.
                section.draftQueue = (section.draftQueue || Promise.resolve()).then(async () => {
                    try {
                        await post(root.dataset.draftUrl, { attemptQuestionId: Number(section.dataset.id), selected, questionVersion: Number(section.dataset.questionVersion) });
                        section.querySelector('.feedback').textContent = 'Đã lưu nháp. Bấm Xác nhận để chốt lựa chọn.';
                    } catch (err) { section.querySelector('.feedback').textContent = err.message; }
                });
            }
        }));
        section.querySelector('.confirm')?.addEventListener('click', () => {
            if (locked(section)) return;
            if (inputs(section).length > 0) {
                if (inputError(section)) { validateInput(); return; }
                return submit(section, []);
            }
            const selected = [...section.querySelectorAll('.option[aria-pressed="true"]')].map(b => Number(b.dataset.option));
            if (selected.length === 0) { section.querySelector('.feedback').textContent = 'Hãy chọn ít nhất một đáp án.'; return; }
            submit(section, selected);
        });
        section.querySelector('.prev').addEventListener('click', () => go(i - 1));
        section.querySelector('.retry').addEventListener('click', () => reopen(section));
        section.querySelector('.next').addEventListener('click', () => {
            if (mastery && sections.every(s => s.dataset.answered === '1')) {
                const next = [...sections.keys()].map(k => (i + 1 + k) % sections.length).find(k => canRetry(sections[k]));
                if (next != null) { go(next); reopen(sections[next]); return; }
            }
            go(i + 1);
        });
        section.querySelector('.skip').addEventListener('click', () => {
            const order = [...sections.keys()].map(k => (i + 1 + k) % sections.length);
            const next = order.find(k => sections[k].dataset.answered !== '1' && !locked(sections[k]) && k !== i);
            go(next ?? i + 1);
        });
        // Thử tải P4 14/11/2026: bấm "đánh dấu" liên tục lúc máy chủ chậm → mỗi lần bấm xếp hàng sau lần trước rồi báo "đang bận".
        // Lần trước chưa xong thì bỏ qua lần bấm mới (đánh dấu là bật/tắt, gửi chồng còn làm đảo ngược ý người dùng).
        const flagButton = section.querySelector('.flag');
        flagButton.addEventListener('click', async () => {
            if (flagButton.getAttribute('aria-busy') === 'true') return;
            flagButton.setAttribute('aria-busy', 'true');
            try {
                await post(root.dataset.flagUrl, { attemptQuestionId: Number(section.dataset.id) });
                section.dataset.flagged = section.dataset.flagged === '1' ? '0' : '1';
                go(i);
            } catch (err) { section.querySelector('.feedback').textContent = err.message; }
            finally { flagButton.removeAttribute('aria-busy'); }
        });
    }
    sections.forEach(initSection);
    root.querySelectorAll('.nav-q').forEach((btn, i) => btn.addEventListener('click', () => go(i)));
    document.getElementById('practice-more')?.addEventListener('click', () => {
        const index = sections.findIndex(canRetry);
        if (index >= 0) { go(index); reopen(sections[index]); }
    });

    if (test) document.addEventListener('submit', async event => {
        if (event.target.id !== 'finish-form' || event.defaultPrevented) return;
        event.preventDefault();
        sections.forEach(flushDraft);
        await Promise.all(sections.map(s => s.draftQueue));
        // data-confirm được site.js hỏi ở sự kiện submit; gửi lại bằng submit() để không hỏi hai lần.
        HTMLFormElement.prototype.submit.call(event.target);
    });

    // Đồng hồ chỉ để hiển thị; máy chủ mới là bên chốt lượt khi hết hạn.
    let deadline = root.dataset.deadline ? new Date(root.dataset.deadline) : null;
    root.addEventListener('learn:state', event => {
        deadline = event.detail.deadlineAt ? new Date(event.detail.deadlineAt) : null;
        document.getElementById('countdown').classList.toggle('d-none', !deadline);
        if (event.detail.time != null) document.getElementById('deadline-text').textContent = event.detail.time ? 'Hạn: ' + event.detail.time : 'Không có hạn';
        // (58B) áp mọi khóa trước khi tìm câu kế tiếp, tránh chuyển tới câu cũng hết giờ.
        const timers = event.detail.questionTimers || [];
        const changed = timers.filter(t => t.locked && sections.some(s => Number(s.dataset.id) === t.id && !locked(s)));
        timers.forEach(t => {
            const s = sections.find(s => Number(s.dataset.id) === t.id);
            if (s) applyClock(s, t, false);
        });
        const expired = changed.find(t => t.id === Number(sections[current].dataset.id)) || changed[0];
        if (expired && !event.detail.ended && !event.detail.moved) questionExpired(sections.find(s => Number(s.dataset.id) === expired.id));
        refresh();
    });
    {
        const el = document.getElementById('countdown');
        el.classList.toggle('d-none', !deadline);
        const tick = () => {
            if (!deadline || root.inert) return;
            const s = Math.floor((deadline - Date.now()) / 1000);
            if (s <= 0) { location.reload(); return; }
            const d = Math.floor(s / 86400), h = Math.floor(s % 86400 / 3600), m = Math.floor(s % 3600 / 60), sec = s % 60;
            el.textContent = 'Còn ' + (d > 0 ? `${d} ngày ` : '') + [h, m, sec].map(n => String(n).padStart(2, '0')).join(':');
        };
        tick();
        setInterval(tick, 1000);
    }

    async function syncClocks() {
        const response = await fetch(root.dataset.stateUrl, { headers: { Accept: 'application/json' }, cache: 'no-store' });
        if (response.ok) root.dispatchEvent(new CustomEvent('learn:state', { detail: await response.json() }));
    }
    // Không aria-live mỗi giây; chỉ thông báo một lần khi máy chủ xác nhận hết giờ.
    if (sections.some(s => s.dataset.limit)) setInterval(async () => {
        if (root.inert) return;
        const due = sections.some(s => displayClock(s) === 0 && s.questionClock?.running && !locked(s));
        if (!due || timerCheck) return;
        timerCheck = true;
        try {
            await syncClocks();
        } catch { /* Mất mạng: giữ số hiển thị, máy chủ kiểm tra khi kết nối lại. */ }
        finally { timerCheck = false; }
    }, 1000);
    root.addEventListener('learn:connected', () => {
        openClock(sections[current]).catch(err => { document.getElementById('question-notice').textContent = err.message; });
    });
    const requested = sections.findIndex(s => location.hash === `#question-${s.dataset.id}`);
    const firstOpen = sections.findIndex(s => s.dataset.answered !== '1' && !locked(s));
    go(requested >= 0 ? requested : firstOpen === -1 ? 0 : firstOpen);
})();
