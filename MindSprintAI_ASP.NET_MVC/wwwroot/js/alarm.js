// Chuông báo giờ học — chuyển từ MindSprint-AI-D-n (script.js checkScheduleReminders/playSyntheticAlarm).
// Giữ cách của nhóm: chỉ báo khi người dùng đã bật; 30 giây kiểm một lần; giờ học đang diễn ra (bắt đầu ≤ bây giờ < kết thúc)
// mà chưa báo thì báo; kiểu báo: chữ (thông báo trình duyệt) / chuông / cả hai / chuông + đọc to; nút "Tắt chuông & Vào học ngay".
// Khác nhóm: MVC tải lại trang khi chuyển trang, nên giờ học "đã báo hôm nay" lưu theo ngày trong localStorage để không kêu lại.
(() => {
    const read = (key, fallback) => { try { return localStorage.getItem(key) ?? fallback; } catch { return fallback; } };
    const write = (key, value) => { try { localStorage.setItem(key, value); } catch { } };
    const settings = {
        get enabled() { return read('mindsprint-alarm-enabled', 'false') === 'true'; },
        set enabled(v) { write('mindsprint-alarm-enabled', String(v)); },
        get style() { const s = read('mindsprint-alarm-style', 'both'); return ['text', 'sound', 'both', 'voice'].includes(s) ? s : 'both'; },
        set style(v) { write('mindsprint-alarm-style', v); }
    };
    const pad = n => String(n).padStart(2, '0');
    const todayKey = () => { const d = new Date(); return `mindsprint-alarm-done-${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`; };
    const done = () => { try { return new Set(JSON.parse(read(todayKey(), '[]'))); } catch { return new Set(); } };

    // Âm báo: hai tiếng bíp 880Hz mỗi giây (như nhóm), tạo bằng Web Audio, không cần file âm thanh.
    let audio = null, ringing = null;
    const beep = () => {
        try {
            const osc = audio.createOscillator(), gain = audio.createGain();
            osc.frequency.value = 880;
            gain.gain.setValueAtTime(0.3, audio.currentTime);
            gain.gain.exponentialRampToValueAtTime(0.01, audio.currentTime + 0.12);
            osc.connect(gain); gain.connect(audio.destination);
            osc.start(); osc.stop(audio.currentTime + 0.12);
        } catch { }
    };
    const ring = () => {
        if (ringing) return;
        try { audio ??= new (window.AudioContext || window.webkitAudioContext)(); audio.resume?.(); } catch { return; }
        ringing = setInterval(() => { beep(); setTimeout(beep, 150); }, 1000);
    };
    const stop = () => { clearInterval(ringing); ringing = null; try { speechSynthesis.cancel(); } catch { } };
    // "Giọng nói AI" của nhóm (bên nhóm chọn vào thì không báo gì vì chưa nối code): đọc to giờ học bằng giọng đọc có sẵn của trình duyệt.
    const speak = slot => {
        try {
            const u = new SpeechSynthesisUtterance(`Đến giờ học rồi. ${slot.topic}, từ ${slot.start} đến ${slot.end}.${slot.note ? ' ' + slot.note : ''}`);
            u.lang = 'vi-VN';
            speechSynthesis.cancel(); speechSynthesis.speak(u);
        } catch { }
    };

    const overlay = () => {
        let dialog = document.getElementById('alarm-dialog');
        if (dialog) return dialog;
        dialog = document.createElement('dialog');
        dialog.id = 'alarm-dialog';
        dialog.className = 'alarm-dialog';
        dialog.setAttribute('aria-labelledby', 'alarm-dialog-title');
        dialog.innerHTML = `<div class="alarm-bell" aria-hidden="true"><svg class="icon"><use href="#i-bell"></use></svg></div>
            <h2 id="alarm-dialog-title">Đến giờ học rồi!</h2>
            <div class="alarm-details"><p class="alarm-time"></p><h3 class="alarm-topic"></h3><p class="alarm-note"></p></div>
            <button type="button" class="btn btn-primary btn-lg" data-alarm-stop>Tắt chuông &amp; vào học ngay</button>`;
        dialog.querySelector('[data-alarm-stop]').addEventListener('click', () => { stop(); dialog.close(); location.href = '/Learn'; });
        dialog.addEventListener('cancel', stop); // phím Esc: tắt chuông, ở lại trang
        document.body.append(dialog);
        return dialog;
    };

    const announce = slot => {
        const style = settings.style;
        if ((style === 'text' || style === 'both') && 'Notification' in window && Notification.permission === 'granted') {
            try {
                const note = new Notification('Đến giờ học MindSprint AI rồi!', {
                    body: `Lịch học: ${slot.start} – ${slot.end} (${slot.topic}${slot.note ? ' · ' + slot.note : ''}). Bấm để vào học.`, tag: `slot-${slot.id}` });
                note.onclick = () => { window.focus(); location.href = '/Learn'; };
            } catch { }
        }
        if (style === 'sound' || style === 'both' || style === 'voice') {
            const dialog = overlay();
            dialog.querySelector('.alarm-time').textContent = `${slot.start} – ${slot.end}`;
            dialog.querySelector('.alarm-topic').textContent = slot.topic;
            dialog.querySelector('.alarm-note').textContent = slot.note || 'Không có ghi chú';
            if (!dialog.open) dialog.showModal();
            ring();
            if (style === 'voice') speak(slot);
        }
    };

    // Học bù (checkMakeupStudyAlert/showMakeupModal của nhóm): giờ học hôm nay đã kết thúc trong lúc không mở MindSprint AI
    // (tắt máy, khóa màn hình) → mở lại thì hiện "Học bù ngay". Như nhóm: chỉ xét trong cùng ngày, không cần bật chuông.
    // Khác nhóm (MVC tải lại trang khi chuyển trang): chỉ coi là "đã không mở" khi lần kiểm trước cách đây hơn 2 phút.
    const lastKey = 'mindsprint-alarm-last-check';
    const makeupDialog = slot => {
        let dialog = document.getElementById('makeup-dialog');
        if (!dialog) {
            dialog = document.createElement('dialog');
            dialog.id = 'makeup-dialog';
            dialog.className = 'alarm-dialog';
            dialog.setAttribute('aria-labelledby', 'makeup-dialog-title');
            dialog.innerHTML = `<div class="alarm-bell" aria-hidden="true"><svg class="icon"><use href="#i-clock"></use></svg></div>
                <h2 id="makeup-dialog-title">Bạn đã lỡ giờ học</h2>
                <p class="alarm-makeup-text"></p>
                <button type="button" class="btn btn-primary btn-lg" data-makeup-go>Học bù ngay bây giờ</button>
                <button type="button" class="btn btn-secondary" data-makeup-later>Để sau</button>`;
            dialog.querySelector('[data-makeup-go]').addEventListener('click', () => { dialog.close(); location.href = '/Learn'; });
            dialog.querySelector('[data-makeup-later]').addEventListener('click', () => dialog.close());
            document.body.append(dialog);
        }
        dialog.querySelector('.alarm-makeup-text').textContent =
            `Giờ học ${slot.topic}${slot.note ? ' (' + slot.note + ')' : ''} lúc ${slot.start} – ${slot.end} hôm nay đã trôi qua trong lúc bạn không mở MindSprint AI. Dành ít phút học bù nhé.`;
        if (!dialog.open) dialog.showModal();
    };
    const minutes = hhmm => Number(hhmm.slice(0, 2)) * 60 + Number(hhmm.slice(3, 5));

    let slots = null;
    const loadSlots = async () => {
        if (slots) return slots;
        try {
            const res = await fetch('/Schedule/Slots', { headers: { 'Accept': 'application/json' } });
            slots = res.ok && res.headers.get('content-type')?.includes('json') ? await res.json() : [];
        } catch { slots = []; }
        return slots;
    };
    const check = async () => {
        const now = new Date(), time = `${pad(now.getHours())}:${pad(now.getMinutes())}`, seen = done();
        const day = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`, last = read(lastKey, '');
        write(lastKey, `${day} ${time}`);
        const lastTime = last.startsWith(day + ' ') ? last.slice(day.length + 1) : null;
        if (lastTime && minutes(time) - minutes(lastTime) > 2) {
            const missed = (await loadSlots()).find(s => s.day === now.getDay() && s.end > lastTime && s.end <= time && !seen.has(s.id));
            if (missed) { seen.add(missed.id); write(todayKey(), JSON.stringify([...seen])); makeupDialog(missed); return; }
        }
        if (!settings.enabled) return;
        await loadSlots();
        for (const slot of slots) {
            if (slot.day !== now.getDay() || time < slot.start || time >= slot.end || seen.has(slot.id)) continue;
            seen.add(slot.id);
            write(todayKey(), JSON.stringify([...seen]));
            announce(slot);
            break;
        }
    };
    check();
    setInterval(check, 30000);

    // Nút điều khiển ở trang Lịch học (như thanh đầu trang thời khóa biểu của nhóm).
    const toggle = document.getElementById('alarm-toggle');
    const styleSelect = document.getElementById('alarm-style');
    const status = document.getElementById('alarm-status');
    const render = () => {
        if (!toggle) return;
        const on = settings.enabled;
        toggle.setAttribute('aria-pressed', String(on));
        toggle.classList.toggle('btn-primary', on);
        toggle.classList.toggle('btn-secondary', !on);
        toggle.querySelector('span').textContent = on ? 'Chuông báo: Bật' : 'Bật chuông báo';
    };
    if (styleSelect) {
        styleSelect.value = settings.style;
        styleSelect.addEventListener('change', () => { settings.style = styleSelect.value; });
    }
    toggle?.addEventListener('click', async () => {
        if (settings.enabled) { settings.enabled = false; render(); return; }
        try { audio ??= new (window.AudioContext || window.webkitAudioContext)(); } catch { } // mở khóa âm thanh bằng chính cú bấm này
        const wantsText = settings.style === 'text' || settings.style === 'both';
        if (wantsText && 'Notification' in window && Notification.permission === 'default') await Notification.requestPermission();
        if (wantsText && 'Notification' in window && Notification.permission === 'denied' && status)
            status.textContent = 'Trình duyệt đang chặn thông báo của trang này; chuông vẫn kêu khi bạn mở MindSprint AI. Muốn nhận thông báo chữ: bấm biểu tượng ổ khóa cạnh thanh địa chỉ → cho phép Thông báo.';
        settings.enabled = true;
        render();
        slots = null;
        check();
    });
    render();
})();
