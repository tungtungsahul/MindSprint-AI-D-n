// Display-only workspace chrome; existing authentication and tab actions own data.
(function () {
    const search = document.getElementById('workspace-search');
    const results = document.getElementById('workspace-search-results');
    const auth = window.MindSprintAuth;
    if (!search || !results || !auth) return;

    function renderHeaderUser(user) {
        closeSearch();
        const name = user?.displayName || user?.email || 'Khách';
        document.getElementById('header-user-name').textContent = name;
        document.getElementById('header-user-email').textContent = user?.email || 'Đăng nhập để lưu tiến độ';
        document.getElementById('header-avatar').textContent = Array.from(name.trim())[0]?.toLocaleUpperCase('vi') || 'K';
        document.getElementById('header-account').setAttribute('aria-label', user ? 'Cài đặt tài khoản của ' + name : 'Đăng nhập');
    }
    renderHeaderUser(auth.getCurrentUser());
    auth.onAuthChange(renderHeaderUser);
    auth.ready.then(renderHeaderUser);

    document.getElementById('header-account').addEventListener('click', () => {
        if (auth.isLoggedIn()) document.querySelector('.sidebar [data-tab=settings]').click();
        else auth.renderAuthModal();
    });
    document.getElementById('header-mail').addEventListener('click', () => document.querySelector('.sidebar [data-tab=notebook]').click());
    document.getElementById('header-notifications').addEventListener('click', () => document.querySelector('.sidebar [data-tab=timetable]').click());
    document.getElementById('tab-content-notebook').addEventListener('click', event => {
        const action = event.target.closest('[data-ui-act]')?.dataset.uiAct;
        const sources = document.querySelector('.nb-sources');
        if (action === 'toggle-sources') {
            sources.classList.toggle('is-expanded');
            sources.querySelector('.nb-source-toggle').setAttribute('aria-expanded', String(sources.classList.contains('is-expanded')));
        }
        if (action === 'focus-upload' || action === 'focus-paste') {
            sources.classList.add('is-expanded');
            sources.querySelector('.nb-source-toggle').setAttribute('aria-expanded', 'true');
            const file = document.getElementById(action === 'focus-paste' ? 'nb-txt-body' : 'nb-file');
            file.closest('details').open = true;
            file.scrollIntoView({block: 'nearest'});
            file.focus();
        }
    });

    function closeSearch() {
        results.hidden = true;
        search.setAttribute('aria-expanded', 'false');
    }
    search.addEventListener('input', () => {
        const query = search.value.trim().toLocaleLowerCase('vi');
        results.replaceChildren();
        if (!query) { closeSearch(); return; }
        const targets = [...document.querySelectorAll('.sidebar .nav-tab, .deck-card, .suggestion-card')]
            .filter(target => target.textContent.toLocaleLowerCase('vi').includes(query)).slice(0, 8);
        for (const target of targets) {
            const button = document.createElement('button');
            button.type = 'button';
            button.textContent = target.querySelector('h3, h4')?.textContent || target.textContent.trim();
            button.addEventListener('click', () => {
                closeSearch();
                if (target.matches('.nav-tab')) { target.click(); target.focus(); }
                else {
                    const tab = target.closest('.tab-pane').id.replace('tab-content-', '');
                    document.querySelector('.sidebar [data-tab=' + tab + ']').click();
                    const studyButton = target.querySelector('.view-deck-btn');
                    if (studyButton) studyButton.click();
                    else target.click();
                }
            });
            results.append(button);
        }
        if (!targets.length) {
            const empty = document.createElement('p');
            empty.textContent = 'Không tìm thấy trang hoặc bộ thẻ đang hiển thị.';
            results.append(empty);
        }
        results.hidden = false;
        search.setAttribute('aria-expanded', 'true');
    });
    search.addEventListener('keydown', event => {
        if (event.key === 'Escape') closeSearch();
        if (event.key === 'ArrowDown' && !results.hidden) { event.preventDefault(); results.querySelector('button')?.focus(); }
        if (event.key === 'Enter' && !results.hidden) { event.preventDefault(); results.querySelector('button')?.click(); }
    });
    results.addEventListener('keydown', event => {
        const buttons = [...results.querySelectorAll('button')];
        const index = buttons.indexOf(document.activeElement);
        if (event.key === 'Escape') { closeSearch(); search.focus(); }
        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            buttons[(index + (event.key === 'ArrowDown' ? 1 : -1) + buttons.length) % buttons.length]?.focus();
        }
    });
    document.addEventListener('click', event => { if (!event.target.closest('.workspace-search')) closeSearch(); });
    document.querySelector('.workspace-search').addEventListener('focusout', event => {
        if (!event.currentTarget.contains(event.relatedTarget)) closeSearch();
    });

    // One short acknowledgement on every click, including repeated decisions.
    for (const id of ['btn-remember', 'btn-forget']) {
        document.getElementById(id)?.addEventListener('click', event => {
            if (matchMedia('(prefers-reduced-motion: reduce)').matches) return;
            event.currentTarget.animate([{transform:'scale(1)'},{transform:'scale(1.05)'},{transform:'scale(1)'}], {duration:150,easing:'ease-out'});
        });
    }
})();
