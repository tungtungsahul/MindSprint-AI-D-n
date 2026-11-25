// (E1) chỉ hỗ trợ nhập; máy chủ kiểm thang điểm và câu thuộc quiz.
(() => {
    const form = document.getElementById('assignment-form');
    if (!form) return;
    const quiz = document.getElementById('QuizId');
    const kind = document.getElementById('Form');
    function refresh() {
        const cardsOnly = kind.value === '2';
        form.querySelector('.critical-settings').disabled = cardsOnly;
        form.querySelectorAll('[data-critical-quiz]').forEach(group => {
            const active = !cardsOnly && group.dataset.criticalQuiz === quiz.value;
            group.hidden = !active;
            group.querySelectorAll('input').forEach(input => input.disabled = !active);
        });
        const noCards = kind.value === '1';
        document.getElementById('flashcard-mode-field').hidden = noCards;
        document.getElementById('FlashcardMode').disabled = noCards;
    }
    quiz.addEventListener('change', refresh);
    kind.addEventListener('change', refresh);
    document.getElementById('Mode').addEventListener('change', refresh);
    const preset = JSON.parse(form.dataset[document.getElementById('Mode').value === '2' ? 'testPreset' : 'practicePreset']);
    form.querySelectorAll('[name="CriticalQuestionIds"]').forEach(input => {
        input.checked = Number(input.closest('[data-critical-quiz]').dataset.criticalQuiz) === preset.CriticalQuizId
            && (preset.CriticalQuestionIds ?? []).includes(Number(input.value));
    });
    refresh();
})();
