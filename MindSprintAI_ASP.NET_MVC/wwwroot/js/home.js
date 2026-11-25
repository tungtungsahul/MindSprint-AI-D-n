// Trang chủ: "Mục tiêu hôm nay" (ô tích tay lưu theo ngày như nhóm; ô đã tự tích do có hoạt động thật thì giữ nguyên)
// và màn ăn mừng mốc chuỗi ngày học (30, 50, 100… như nhóm; mỗi mốc hiện một lần).
(() => {
    const read = (k, d) => { try { return JSON.parse(localStorage.getItem(k)) ?? d; } catch { return d; } };
    const write = (k, v) => { try { localStorage.setItem(k, JSON.stringify(v)); } catch { } };

    const goals = document.getElementById('home-goals');
    if (goals) {
        const key = `mindsprint-daily-goals-${goals.dataset.day}`;
        const inputs = [...goals.querySelectorAll('[data-daily-goal]')];
        const saved = read(key, {});
        inputs.forEach(i => { if (saved[i.dataset.dailyGoal]) i.checked = true; });
        const update = () => {
            const done = inputs.filter(i => i.checked).length, percent = Math.round(done * 100 / inputs.length);
            document.getElementById('home-goals-count').textContent = `${done}/${inputs.length} xong`;
            const ring = document.getElementById('home-goal-ring');
            ring?.style.setProperty('--p', percent);
            ring?.setAttribute('aria-label', `Hoàn thành ${percent}% mục tiêu hôm nay`);
            const value = document.getElementById('home-goal-value'); if (value) value.textContent = `${percent}%`;
            write(key, Object.fromEntries(inputs.map(i => [i.dataset.dailyGoal, i.checked])));
        };
        inputs.forEach(i => i.addEventListener('change', update));
        update();
    }

    const dialog = document.getElementById('streak-dialog');
    const streak = Number(dialog?.dataset.streak ?? 0);
    const milestones = [30, 50, 100, 200, 300, 365, 400, 500, 600, 700, 800, 900, 1000];
    if (dialog && milestones.includes(streak)) {
        const celebrated = read('mindsprint-celebrated-milestones', []);
        if (!celebrated.includes(streak)) {
            write('mindsprint-celebrated-milestones', [...celebrated, streak]);
            dialog.showModal();
        }
    }
})();
