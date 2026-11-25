// Game ghép thẻ — luật của nhóm (script.js startGame/handleGameCardClick/endGame): tối thiểu 3 từ, tối đa 6 cặp ngẫu nhiên,
// chọn một ô mỗi cột; đúng thì khóa cặp, sai thì báo rồi bỏ chọn; đồng hồ 0,1 giây; kỷ lục theo chủ đề lưu ở trình duyệt này.
(() => {
    const board = document.getElementById('game');
    const data = document.getElementById('game-cards');
    if (!board || !data) return;
    const all = JSON.parse(data.textContent);
    const left = document.getElementById('game-left'), right = document.getElementById('game-right');
    const timeEl = document.getElementById('game-time'), bestEl = document.getElementById('game-best'), result = document.getElementById('game-result');
    const bestKey = `mindsprint-game-best-${board.dataset.key}`;
    const readBest = () => { try { return localStorage.getItem(bestKey); } catch { return null; } };
    let timer = null, time = 0, selected = [];
    const shuffle = list => { const a = [...list]; for (let i = a.length - 1; i > 0; i--) { const j = Math.floor(Math.random() * (i + 1)); [a[i], a[j]] = [a[j], a[i]]; } return a; };

    const tile = (text, id, side) => {
        const b = document.createElement('button');
        b.type = 'button'; b.className = 'game-card'; b.textContent = text; b.dataset.id = id; b.dataset.side = side;
        if (text.length > 100) b.classList.add('is-long'); else if (text.length > 40) b.classList.add('is-medium');
        b.addEventListener('click', () => pick(b));
        return b;
    };

    const start = () => {
        result.textContent = '';
        if (all.length < 3) { result.textContent = 'Chủ đề này có quá ít từ (cần ít nhất 3). Hãy chọn chủ đề khác.'; return; }
        const deck = shuffle(all).slice(0, Math.min(6, all.length)).map((c, i) => ({ ...c, id: i }));
        left.replaceChildren(...shuffle(deck).map(c => tile(c.q, c.id, 'left')));
        right.replaceChildren(...shuffle(deck).map(c => tile(c.a, c.id, 'right')));
        selected = []; time = 0; timeEl.textContent = '0.0';
        bestEl.textContent = readBest() ? readBest() + 's' : '--';
        clearInterval(timer);
        timer = setInterval(() => { time += 0.1; timeEl.textContent = time.toFixed(1); }, 100);
    };

    const pick = el => {
        if (el.classList.contains('is-matched') || selected.length >= 2) return;
        // Mỗi lượt một ô bên trái + một ô bên phải; bấm lại ô cùng cột thì đổi lựa chọn.
        const same = selected.find(s => s.dataset.side === el.dataset.side);
        if (same) { same.classList.remove('is-selected'); selected = selected.filter(s => s !== same); if (same === el) return; }
        el.classList.add('is-selected');
        selected.push(el);
        if (selected.length < 2) return;
        const [a, b] = selected;
        if (a.dataset.id === b.dataset.id) {
            [a, b].forEach(x => { x.classList.remove('is-selected'); x.classList.add('is-matched'); x.disabled = true; });
            selected = [];
            if (!board.querySelector('.game-card:not(.is-matched)')) finish();
        } else {
            [a, b].forEach(x => x.classList.add('is-wrong'));
            setTimeout(() => { [a, b].forEach(x => x.classList.remove('is-selected', 'is-wrong')); selected = []; }, 600);
        }
    };

    const finish = () => {
        clearInterval(timer);
        const best = parseFloat(readBest() ?? 'Infinity');
        const record = time < best;
        if (record) { try { localStorage.setItem(bestKey, time.toFixed(1)); } catch { } bestEl.textContent = time.toFixed(1) + 's'; }
        result.textContent = `Hoàn thành trong ${time.toFixed(1)} giây${record ? ' · Kỷ lục mới!' : ''}`;
    };

    document.getElementById('game-restart').addEventListener('click', start);
    start();
})();
