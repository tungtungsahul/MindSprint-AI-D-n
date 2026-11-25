// JSON nằm trong data-* do Razor mã hóa; không nhúng nội dung người dùng vào chuỗi JavaScript.
(() => {
    function apply(form, preset) {
        Object.entries(preset).forEach(([name, value]) => {
            const input = form.querySelector(`[name="${name}"]`);
            if (!input) return;
            if (input.type === 'checkbox') input.checked = value === true;
            else input.value = value ?? '';
        });
    }
    const assignment = document.getElementById('assignment-form');
    // (H8) để trống là tắt; không gửi ngưỡng khi giám sát đang tắt.
    const monitoring = document.getElementById('MonitorScreen');
    const limit = document.getElementById('AutoSubmitLeaves');
    monitoring?.addEventListener('change', () => { if (!monitoring.checked && limit) limit.value = ''; });
    if (assignment?.dataset.practicePreset) {
        const practice = JSON.parse(assignment.dataset.practicePreset);
        const test = JSON.parse(assignment.dataset.testPreset);
        const mode = document.getElementById('Mode');
        mode.addEventListener('change', () => {
            apply(assignment, mode.value === '2' ? test : practice);
            document.getElementById('reset-assignment-mode').value = mode.value;
        });
    }
    const question = document.getElementById('question-form');
    if (question?.dataset.newQuestion === 'true') {
        const presets = JSON.parse(question.dataset.questionPresets);
        const type = document.getElementById('Type');
        type.addEventListener('change', () => {
            apply(question, presets[type.value] ?? {});
            document.getElementById('reset-question-type').value = type.value;
        });
    }
})();
