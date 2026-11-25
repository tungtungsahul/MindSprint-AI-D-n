(() => {
    const mode = document.getElementById('Mode');
    const kind = document.getElementById('Form');
    if (!mode || !kind) return;
    const setVisible = (id, visible) => document.getElementById(id).classList.toggle('d-none', !visible);
    function refresh() {
        const test = mode.value === '2';
        if (test) kind.value = '1';
        kind.disabled = test;
        setVisible('assignment-form-kind', !test);
        setVisible('required-parts-field', !test && kind.value === '3');
        setVisible('study-set-field', !test && kind.value === '2');
        setVisible('cards-after-due-field', !test && kind.value !== '1');
        setVisible('quiz-field', kind.value !== '2');
        document.getElementById('QuizId').disabled = kind.value === '2';
        document.getElementById('QuizId').required = kind.value !== '2';
        document.getElementById('StudySetId').disabled = kind.value !== '2';
    }
    mode.addEventListener('change', refresh);
    kind.addEventListener('change', refresh);
    refresh();
})();
