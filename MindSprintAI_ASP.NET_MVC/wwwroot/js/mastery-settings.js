(() => {
    const root = document.querySelector('[data-mastery-settings]');
    if (!root) return;
    const threshold = root.querySelector('[name="MasteryThreshold"]');
    const limit = root.querySelector('[name="AnswersPerQuestion"]');
    const mode = document.getElementById('Mode');
    const kind = document.getElementById('Form');
    function refresh() {
        const visible = mode?.value !== '2' && kind?.value !== '2';
        root.classList.toggle('d-none', !visible);
        threshold.disabled = !visible;
        root.querySelectorAll('[data-mastery-dependent]').forEach(el => {
            const enabled = visible && threshold.value !== '';
            el.classList.toggle('d-none', !enabled);
            el.querySelectorAll('input').forEach(input => input.disabled = !enabled);
        });
        root.querySelector('[data-mastery-warning]').classList.toggle('d-none', !limit.value);
    }
    [mode, kind, threshold, limit].forEach(el => el?.addEventListener('change', refresh));
    refresh();
})();
