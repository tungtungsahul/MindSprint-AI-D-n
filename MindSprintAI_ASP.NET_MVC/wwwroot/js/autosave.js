// Chỉ nâng cấp nhóm cài đặt được đánh dấu; HTML vẫn gửi bình thường nếu không chạy được JS.
(() => {
    if (!window.fetch || !window.FormData || !window.DOMParser) return;
    for (const form of document.querySelectorAll('form[data-autosave]')) {
        if (form.hasAttribute('data-confirm')) continue;
        const controls = [...form.querySelectorAll('input[type="checkbox"], input[type="radio"], select')];
        if (!controls.length) continue;
        const status = document.createElement('div');
        status.className = 'small mt-2 d-flex flex-wrap gap-2 align-items-center';
        status.setAttribute('role', 'status');
        status.setAttribute('aria-live', 'polite');
        status.setAttribute('aria-atomic', 'true');
        form.append(status);
        let busy = false, timer, fadeTimer, retryValues;
        const snapshot = () => controls.map(control => control.tagName === 'SELECT'
            ? [...control.options].map(option => option.selected) : control.checked);
        function restore(values) {
            controls.forEach((control, i) => {
                if (control.tagName === 'SELECT') [...control.options].forEach((option, j) => option.selected = values[i][j]);
                else control.checked = values[i];
            });
        }
        let saved = snapshot();
        function show(message, icon, retry = false) {
            clearTimeout(fadeTimer);
            status.classList.remove('text-muted');
            status.replaceChildren();
            const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            svg.setAttribute('class', 'icon');
            svg.setAttribute('aria-hidden', 'true');
            svg.setAttribute('width', '20');
            svg.setAttribute('height', '20');
            const use = document.createElementNS('http://www.w3.org/2000/svg', 'use');
            use.setAttribute('href', '#i-' + icon);
            svg.append(use);
            const text = document.createElement('span');
            text.textContent = message;
            status.append(svg, text);
            if (retry) {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'btn btn-sm btn-outline-secondary';
                button.textContent = 'Thử lại';
                button.addEventListener('click', () => {
                    if (busy || !retryValues) return;
                    restore(retryValues);
                    save();
                });
                status.append(button);
            }
        }
        // Nhãn trạng thái bên cạnh công tắc cũng phải phản ánh giá trị đã lưu.
        function updateAccountBadges() {
            const card = form.closest('.acc-card');
            if (!card) return;
            const switches = [...card.querySelectorAll('form[data-autosave] input[type="checkbox"]')];
            for (const input of switches) {
                const badge = input.closest('.acc-subcard')?.querySelector('.acc-subhead .acc-state');
                if (!badge) continue;
                const enabled = input.checked;
                badge.classList.toggle('is-on', enabled);
                badge.classList.toggle('is-off', !enabled);
                const icon = badge.querySelector('svg')?.cloneNode(true);
                icon?.querySelector('use')?.setAttribute('href', enabled ? '#i-check' : '#i-x');
                badge.replaceChildren(...(icon ? [icon] : []), document.createTextNode(enabled ? 'Đã bật' : 'Chưa bật'));
            }
            const badge = card.querySelector('.acc-card-top .acc-state');
            if (badge) {
                const count = switches.filter(input => input.checked).length;
                const icon = badge.querySelector('svg')?.cloneNode(true);
                icon?.querySelector('use')?.setAttribute('href', count ? '#i-check' : '#i-bell-off');
                badge.classList.toggle('is-on', count > 0);
                badge.classList.toggle('is-off', count === 0);
                badge.replaceChildren(...(icon ? [icon] : []), document.createTextNode(count ? `Đã bật (${count}/${switches.length})` : 'Chưa bật'));
            }
        }
        async function save() {
            clearTimeout(timer);
            if (busy || !form.reportValidity()) return;
            busy = true;
            retryValues = undefined;
            const attempted = snapshot();
            // Lấy cả antiforgery và hidden false trước khi tắt các trường.
            const body = new FormData(form);
            const disabled = [...form.elements].map(control => [control, control.disabled]);
            disabled.forEach(([control]) => control.disabled = true);
            form.setAttribute('aria-busy', 'true');
            show('Đang lưu…', 'clock');
            let failureText = 'Chưa lưu được. Thử lại', reloadPending = false;
            try {
                const response = await fetch(form.action, { method: 'POST', body, credentials: 'same-origin' });
                const page = new DOMParser().parseFromString(await response.text(), 'text/html');
                const errors = [...page.querySelectorAll('.alert-danger, .validation-summary-errors, .field-validation-error')];
                const errorText = [...new Set(errors.map(error => error.textContent.trim()).filter(Boolean))].join('\n');
                if (errors.length) {
                    failureText = errorText || failureText;
                    throw new Error(failureText);
                }
                if (!response.ok) throw new Error('Chưa lưu được. Thử lại');
                // Đăng nhập hết hạn có thể trả trang đăng nhập 200 sau chuyển hướng.
                const returnedForm = [...page.querySelectorAll('form[data-autosave]')].find(candidate =>
                    new URL(candidate.getAttribute('action'), response.url).pathname === new URL(form.action).pathname);
                if (!returnedForm) {
                    failureText = 'Chưa lưu được. Hãy tải lại trang rồi thử lại.';
                    throw new Error(failureText);
                }
                saved = attempted;
                const summaries = returnedForm.querySelectorAll('[data-autosave-summary]');
                form.querySelectorAll('[data-autosave-summary]').forEach((summary, i) => {
                    if (summaries[i]) summary.textContent = summaries[i].textContent;
                });
                updateAccountBadges();
                show('Đã lưu', 'check');
                reloadPending = form.dataset.autosave === 'reload';
                if (reloadPending) document.getElementById('cards')?.setAttribute('inert', '');
                fadeTimer = setTimeout(() => {
                    status.classList.add('text-muted');
                    if (form.dataset.autosave === 'reload') window.location.reload();
                }, 2000);
            } catch {
                retryValues = attempted;
                restore(saved);
                updateAccountBadges();
                const fallback = 'Chưa lưu được. Thử lại';
                show(failureText === fallback ? fallback : `${failureText}\n${fallback}`, 'x', true);
            } finally {
                if (!reloadPending) disabled.forEach(([control, wasDisabled]) => control.disabled = wasDisabled);
                form.removeAttribute('aria-busy');
                busy = reloadPending;
            }
        }
        form.addEventListener('change', event => {
            if (busy || !controls.includes(event.target) || event.target.matches(':disabled')) return;
            retryValues = undefined;
            clearTimeout(timer);
            clearTimeout(fadeTimer);
            status.replaceChildren();
            timer = setTimeout(save, 400);
        });
        form.addEventListener('submit', event => { event.preventDefault(); save(); });
        for (const submitter of form.elements) {
            if (submitter.type === 'submit') submitter.hidden = true;
        }
    }
})();
