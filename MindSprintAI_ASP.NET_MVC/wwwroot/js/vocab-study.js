// Ôn thẻ từ vựng hệ thống trong hộp như nhóm: lật thẻ, đánh dấu Chưa thuộc/Đã thuộc (lưu ở trình duyệt theo chủ đề), đi trước/sau.
(() => {
    const modal = document.getElementById('study-modal');
    if (!modal) return;
    const cards = [...document.querySelectorAll('.vocab-item')].map(li => ({
        front: li.querySelector('strong')?.textContent ?? '', back: li.querySelector('span')?.textContent ?? '', example: li.querySelector('small')?.textContent ?? ''
    }));
    const key = `mindsprint-vocab-known-${modal.dataset.topic}`;
    let known = {}; try { known = JSON.parse(localStorage.getItem(key)) ?? {}; } catch { }
    const $ = s => modal.querySelector(s);
    let index = 0, flipped = false;

    const render = () => {
        const c = cards[index];
        $('[data-study-count]').textContent = `Thẻ ${index + 1} / ${cards.length}`;
        $('[data-study-bar]').style.width = `${(index + 1) * 100 / cards.length}%`;
        $('[data-study-front]').textContent = flipped ? c.back : c.front;
        $('[data-study-back]').hidden = !flipped;
        $('[data-study-back]').textContent = flipped ? c.front : '';
        $('[data-study-example]').hidden = !flipped || !c.example;
        $('[data-study-example]').textContent = c.example;
        $('[data-study-hint]').lastChild.textContent = flipped ? 'Nhấp để lật lại câu hỏi' : 'Nhấp để lật xem đáp án';
        $('[data-study-rate]').hidden = !flipped;
        $('[data-study-card]').classList.toggle('is-flipped', flipped);
        const count = cards.filter(x => known[x.front] === 1).length;
        $('[data-study-known]').textContent = `Đã thuộc ${count}/${cards.length}` + (known[c.front] === 1 ? ' · thẻ này đã thuộc' : known[c.front] === 0 ? ' · thẻ này chưa thuộc' : '');
    };
    const go = step => { index = (index + step + cards.length) % cards.length; flipped = false; render(); };

    const card = $('[data-study-card]');
    card.addEventListener('click', () => (window.MindSprintFlip ?? ((_, f) => f()))(card, () => { flipped = !flipped; render(); }));
    modal.querySelectorAll('[data-step]').forEach(b => b.addEventListener('click', () => go(Number(b.dataset.step))));
    modal.querySelectorAll('[data-rate]').forEach(b => b.addEventListener('click', () => {
        known[cards[index].front] = Number(b.dataset.rate);
        try { localStorage.setItem(key, JSON.stringify(known)); } catch { }
        go(1);
    }));
    modal.addEventListener('keydown', e => {
        if (e.key === 'ArrowLeft') go(-1); else if (e.key === 'ArrowRight') go(1);
    });
    modal.addEventListener('show.bs.modal', () => { index = 0; flipped = false; render(); });
    if (cards.length) render();
})();
