// Trình soạn câu chọn một/chọn nhiều. Máy chủ vẫn kiểm tra lại mọi quy tắc; ở đây chỉ để báo lỗi sớm.
(() => {
    const form = document.getElementById('question-form');
    if (!form) return;
    const box = document.getElementById('options');
    const type = document.getElementById('Type');
    const error = document.getElementById('form-error');
    const MIN = 2, MAX = 6, SINGLE = '1';

    const rows = () => [...box.querySelectorAll('.option-row')];

    // Đặt lại chỉ số Options[i] sau khi thêm/bớt dòng để máy chủ nhận đúng danh sách.
    const renumber = () => rows().forEach((row, i) => {
        row.querySelector('.correct').name = `Options[${i}].IsCorrect`;
        const text = row.querySelector('.option-text');
        text.name = `Options[${i}].Text`;
        text.placeholder = `Lựa chọn ${i + 1}`;
        text.id = `option-text-${i}`;
        const label = row.querySelector('.option-label');
        label.htmlFor = text.id;
        label.textContent = text.placeholder;
        row.querySelector('.correct').id = `option-correct-${i}`;
        row.querySelector('.correct-label').htmlFor = `option-correct-${i}`;
    });

    // Câu chọn một: tick đáp án mới thì tự bỏ tick đáp án cũ (B4c).
    box.addEventListener('change', e => {
        if (!e.target.classList.contains('correct') || type.value !== SINGLE || !e.target.checked) return;
        box.querySelectorAll('.correct').forEach(c => { if (c !== e.target) c.checked = false; });
    });
    type.addEventListener('change', () => {
        if (type.value !== SINGLE) return;
        let kept = false;
        box.querySelectorAll('.correct').forEach(c => { if (c.checked) { c.checked = !kept; kept = true; } });
    });

    const blankBox = document.getElementById('blanks');
    const blankRows = () => [...blankBox.querySelectorAll('.blank-row')];
    function renumberBlanks() {
        blankRows().forEach((row, i) => {
            row.querySelector('.blank-label').textContent = `Ô ${i + 1}`;
            const input = row.querySelector('input');
            input.name = `BlankAnswers[${i}]`;
            input.setAttribute('aria-label', `Đáp án ô ${i + 1}`);
            input.id = `blank-answer-${i}`;
            row.querySelector('.blank-label').htmlFor = input.id;
        });
    }
    document.getElementById('add-blank').addEventListener('click', () => {
        if (blankRows().length >= 5) { error.textContent = 'Tối đa 5 ô.'; return; }
        const row = blankRows()[0].cloneNode(true);
        row.querySelector('input').value = '';
        blankBox.appendChild(row);
        renumberBlanks();
    });
    blankBox.addEventListener('click', event => {
        if (!event.target.classList.contains('remove-blank')) return;
        if (blankRows().length <= 1) { error.textContent = 'Cần ít nhất 1 ô.'; return; }
        event.target.closest('.blank-row').remove();
        renumberBlanks();
    });
    function fields() {
        const survey = ['11', '12', '13'].includes(type.value);
        for (const [selector, visible] of [['[data-choice-field]', ['1', '2', '11', '12'].includes(type.value)], ['[data-text-field]', type.value === '3'], ['[data-number-field]', type.value === '4'], ['[data-points-field]', !survey], ['[data-survey-field]', survey]]) {
            form.querySelectorAll(selector).forEach(el => {
                el.classList.toggle('d-none', !visible);
                el.querySelectorAll('input, select, button').forEach(input => input.disabled = !visible);
            });
        }
        // Có thể đang sửa câu điền rồi đổi về câu chọn đáp án.
        while (rows().length < MIN) {
            const row = document.createElement('div');
            row.className = 'input-group mb-2 option-row';
            row.innerHTML = '<label class="form-label option-label"></label><div class="input-group-text"><label class="small me-2 correct-label">Đáp án đúng</label><input class="form-check-input correct" type="checkbox" value="true" aria-label="Đáp án đúng"></div><input class="form-control option-text"><button type="button" class="btn btn-outline-secondary remove-option" aria-label="Bỏ lựa chọn">✕</button>';
            box.appendChild(row);
        }
        renumber();
        box.querySelectorAll('.correct').forEach(input => {
            input.disabled = survey;
            input.closest('.input-group-text').classList.toggle('d-none', survey);
        });
    }
    type.addEventListener('change', fields);
    fields();

    const editKind = document.getElementById('EditKind');
    const extraMinutes = document.getElementById('ExtraSessionMinutes');
    function editFields() {
        if (extraMinutes) extraMinutes.disabled = editKind?.value !== '2';
    }
    editKind?.addEventListener('change', editFields);
    editFields();

    box.addEventListener('click', e => {
        if (!e.target.classList.contains('remove-option')) return;
        if (rows().length <= MIN) { error.textContent = `Cần ít nhất ${MIN} lựa chọn.`; return; }
        e.target.closest('.option-row').remove();
        renumber();
    });

    document.getElementById('add-option').addEventListener('click', () => {
        if (rows().length >= MAX) { error.textContent = `Tối đa ${MAX} lựa chọn.`; return; }
        const copy = rows()[0].cloneNode(true);
        copy.querySelector('.correct').checked = false;
        copy.querySelector('.option-text').value = '';
        box.appendChild(copy);
        renumber();
        fields();
    });

    form.addEventListener('submit', e => {
        const seconds = document.getElementById('TimeLimitSeconds').value;
        if (seconds !== '' && (!Number.isInteger(Number(seconds)) || Number(seconds) < 5 || Number(seconds) > 3600)) {
            e.preventDefault(); error.textContent = 'Giờ riêng phải từ 5 đến 3600 giây, hoặc để trống.'; return;
        }
        if (type.value === '3') {
            const missing = blankRows().findIndex(r => !r.querySelector('input').value.split('|').some(a => a.trim()));
            if (missing >= 0) { e.preventDefault(); error.textContent = `Ô ${missing + 1} chưa có đáp án.`; }
            return;
        }
        if (type.value === '4' || type.value === '13') return;
        const filled = rows().filter(r => r.querySelector('.option-text').value.trim() !== '');
        const correct = filled.filter(r => r.querySelector('.correct').checked).length;
        let message = '';
        if (filled.length < MIN) message = `Cần ít nhất ${MIN} lựa chọn có nội dung.`;
        else if (['1', '2'].includes(type.value) && rows().some(r => r.querySelector('.correct').checked && r.querySelector('.option-text').value.trim() === ''))
            message = 'Có đáp án đúng đang để trống nội dung.';
        else if (['1', '2'].includes(type.value) && correct === 0) message = 'Hãy tick ít nhất một đáp án đúng.';
        if (message) { e.preventDefault(); error.textContent = message; }
    });
})();
