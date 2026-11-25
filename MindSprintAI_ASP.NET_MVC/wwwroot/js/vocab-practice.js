// Trắc nghiệm / tự luận từ vựng — luật nhóm (script.js renderQuizQuestion/checkAnswer): từng câu, đúng +1 điểm,
// tự luận so khớp không phân biệt hoa thường, cuối bài liệt kê câu sai để ôn lại. Không lưu điểm (ôn tập tự do như nhóm).
(() => {
    const board = document.getElementById('practice');
    const data = document.getElementById('practice-deck');
    if (!board || !data) return;
    const deck = JSON.parse(data.textContent);
    const write = board.dataset.mode === 'write';
    const $ = id => document.getElementById(id);
    const norm = s => s.trim().toLocaleLowerCase('vi').replace(/\s+/g, ' ');
    let i = 0, score = 0, answered = false;
    const wrong = [];

    const show = () => {
        const q = deck[i];
        answered = false;
        $('pr-num').textContent = `Câu hỏi ${i + 1} / ${deck.length}`;
        $('pr-bar').style.width = `${(i / deck.length) * 100}%`;
        $('pr-feedback').textContent = ''; $('pr-feedback').className = 'practice-feedback';
        $('pr-next').hidden = true;
        $('pr-label').textContent = write ? 'Gõ từ vựng tiếng Anh tương ứng với nghĩa:' : 'Chọn nghĩa phù hợp cho từ:';
        $('pr-word').textContent = write ? q.meaning : q.word;
        $('pr-hint').textContent = write ? (q.hint ?? `Gợi ý: từ bắt đầu bằng chữ "${q.word.charAt(0)}"`) : (q.hint ? `Gợi ý: ${q.hint}` : '');
        $('pr-write').hidden = !write;
        $('pr-options').replaceChildren(...(write ? [] : q.options.map((text, n) => {
            const b = document.createElement('button');
            b.type = 'button'; b.className = 'practice-option'; b.textContent = text; b.dataset.letter = 'ABCD'[n];
            b.addEventListener('click', () => answer(text, b));
            return b;
        })));
        if (write) { $('pr-input').value = ''; $('pr-input').focus(); }
    };

    const answer = (given, button) => {
        if (answered) return;
        answered = true;
        const q = deck[i];
        const ok = write ? norm(given) === norm(q.word) : given === q.meaning;
        if (ok) score++; else wrong.push(q);
        $('pr-score').textContent = score;
        $('pr-feedback').textContent = ok ? 'Chính xác!' : `Chưa đúng. Đáp án: ${write ? q.word : q.meaning}`;
        $('pr-feedback').className = `practice-feedback ${ok ? 'is-right' : 'is-wrong'}`;
        board.querySelectorAll('.practice-option').forEach(b => {
            b.disabled = true;
            if (b.textContent === q.meaning) b.classList.add('is-right');
            else if (b === button) b.classList.add('is-wrong');
        });
        $('pr-next').hidden = false;
        $('pr-next').textContent = i + 1 < deck.length ? 'Câu tiếp theo' : 'Xem kết quả';
        $('pr-next').focus();
    };

    $('pr-write').addEventListener('submit', e => { e.preventDefault(); answer($('pr-input').value, null); });
    $('pr-next').addEventListener('click', () => {
        if (++i < deck.length) { show(); return; }
        $('pr-bar').style.width = '100%';
        ['pr-label', 'pr-word', 'pr-hint', 'pr-options', 'pr-write', 'pr-feedback', 'pr-next'].forEach(id => $(id).hidden = true);
        const result = $('pr-result');
        result.hidden = false;
        const title = document.createElement('h2'); title.textContent = `Bạn đúng ${score}/${deck.length} câu (${Math.round(score * 100 / deck.length)}%)`;
        const list = document.createElement('ul');
        wrong.forEach(q => { const li = document.createElement('li'); li.textContent = `${q.word} — ${q.meaning}`; list.append(li); });
        const again = document.createElement('a'); again.className = 'btn btn-primary'; again.href = location.href; again.textContent = 'Làm bài mới';
        result.replaceChildren(title, ...(wrong.length ? [Object.assign(document.createElement('p'), { textContent: 'Từ cần ôn lại:' }), list] : []), again);
    });
    show();
})();
