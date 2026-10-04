// Shared Auth Module - dùng chung cho toàn app
// Xử lý đăng nhập, đăng ký, đăng xuất, kiểm tra trạng thái đăng nhập
(function () {
    const api = window.MindSprintApi;
    if (!api) return;

    let currentUser = null;
    let authListeners = [];
    const AUTH_STORAGE_KEY = 'ms-user';

    function normalizeUser(user) {
        if (!user) return null;
        return { id: user.id ?? user.Id, email: user.email ?? user.Email, displayName: user.displayName ?? user.DisplayName ?? user.email ?? user.Email };
    }

    function notifyAuthChange() {
        const user = currentUser;
        const updates = authListeners.map(fn => {
            try { return Promise.resolve(fn(user)); }
            catch (error) { return Promise.reject(error); }
        });
        return Promise.allSettled(updates).then(results => {
            results.filter(result => result.status === 'rejected').forEach(result => console.error('Không thể cập nhật trạng thái đăng nhập:', result.reason));
        });
    }

    function saveUser(user) {
        currentUser = normalizeUser(user);
        if (user) {
            localStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(currentUser));
        } else {
            localStorage.removeItem(AUTH_STORAGE_KEY);
        }
        return notifyAuthChange();
    }

    function loadSavedUser() {
        const saved = localStorage.getItem(AUTH_STORAGE_KEY);
        if (saved) {
            try {
                currentUser = normalizeUser(JSON.parse(saved));
            } catch {
                currentUser = null;
            }
        }
    }

    async function login(email, password) {
        const user = await api.login(email, password);
        await saveUser(user);
        return user;
    }

    async function demoLogin() {
        const user = await api.demoLogin();
        await saveUser(user);
        return user;
    }

    async function register(email, password, displayName) {
        const user = await api.register(email, password, displayName);
        await saveUser(user);
        return user;
    }

    function logout() {
        const revocation = api.isLoggedIn() ? api.revokeCurrentSession().catch(() => {}) : Promise.resolve();
        api.logout();
        return revocation;
    }

    function isLoggedIn() {
        return !!currentUser && api.isLoggedIn();
    }

    function getCurrentUser() {
        return currentUser;
    }

    function onAuthChange(listener) {
        authListeners.push(listener);
        if (currentUser) listener(currentUser);
        return () => {
            authListeners = authListeners.filter(l => l !== listener);
        };
    }

    function renderAuthModal(onSuccess) {
        const existing = document.querySelector('.auth-overlay');
        if (existing) { existing.querySelector('.auth-input')?.focus(); return; }
        let mode = 'login';
        let overlay = null;
        let submitting = false;
        const previousFocus = document.activeElement;
        const previousOverflow = document.body.style.overflow;

        function close() {
            if (submitting) return;
            overlay?.remove();
            overlay = null;
            document.body.style.overflow = previousOverflow;
            previousFocus?.focus();
        }

        function buildHTML() {
            const isLogin = mode === 'login';
            return `
            <div class="auth-overlay-bg" data-act="close-bg"></div>
            <div class="auth-card" role="dialog" aria-modal="true" aria-labelledby="auth-dialog-title">
                <!-- Header -->
                <div class="auth-card-header">
                    <button class="auth-close-btn" data-act="close" title="Đóng">&#10005;</button>
                    <div class="auth-logo-row">
                        <div class="auth-logo-icon"><i class="fas fa-copy"></i></div>
                        <span class="auth-logo-name">Flashcard</span>
                    </div>
                    <h2 class="auth-header-title" id="auth-dialog-title">${isLogin ? 'Chào mừng trở lại! 👋' : 'Tạo tài khoản mới'}</h2>
                    <p class="auth-header-sub">${isLogin ? 'Đăng nhập để tiếp tục hành trình học tập.' : 'Đăng ký miễn phí và bắt đầu học ngay hôm nay.'}</p>
                </div>

                <!-- Body -->
                <div class="auth-card-body">
                    <!-- Tab switcher -->
                    <div class="auth-tab-row">
                        <button class="auth-tab-btn ${isLogin ? 'active' : ''}" data-act="tab-login">
                            <i class="fas fa-sign-in-alt"></i> Đăng nhập
                        </button>
                        <button class="auth-tab-btn ${!isLogin ? 'active' : ''}" data-act="tab-register">
                            <i class="fas fa-user-plus"></i> Đăng ký
                        </button>
                    </div>

                    <!-- Fields -->
                    <div class="auth-field-group" id="auth-field-group">
                        ${!isLogin ? `
                        <div class="auth-field">
                            <i class="fas fa-user auth-field-icon"></i>
                            <input class="auth-input" id="auth-name" type="text" placeholder="Tên hiển thị của bạn" maxlength="60" autocomplete="name">
                        </div>` : ''}

                        <div class="auth-field">
                            <i class="fas fa-envelope auth-field-icon"></i>
                            <input class="auth-input" id="auth-email" type="email" placeholder="Địa chỉ email" autocomplete="email">
                        </div>

                        <div class="auth-field">
                            <i class="fas fa-lock auth-field-icon"></i>
                            <input class="auth-input" id="auth-pass" type="password" placeholder="Mật khẩu (tối thiểu 6 ký tự)" autocomplete="${isLogin ? 'current-password' : 'new-password'}" style="padding-right:3rem">
                            <button class="auth-pw-toggle" data-act="toggle-pw" type="button" tabindex="-1" title="Hiện/ẩn mật khẩu">
                                <i class="fas fa-eye" id="pw-eye-icon"></i>
                            </button>
                        </div>
                    </div>

                    <!-- Error -->
                    <div id="auth-error-box" style="display:none" class="auth-error-msg" role="alert">
                        <i class="fas fa-exclamation-circle"></i>
                        <span id="auth-error-text"></span>
                    </div>

                    <!-- Submit -->
                    <button class="auth-submit-btn" id="auth-submit-btn" data-act="submit">
                        <i class="fas ${isLogin ? 'fa-sign-in-alt' : 'fa-user-plus'}"></i>
                        ${isLogin ? 'Đăng nhập' : 'Tạo tài khoản'}
                    </button>

                    <!-- Divider -->
                    <div class="auth-divider">hoặc</div>

                    <!-- Demo login -->
                    <button class="auth-demo-btn" data-act="demo">
                        <i class="fas fa-flask"></i> Thử với tài khoản Demo
                    </button>

                    <!-- Footer note -->
                    <p class="auth-footer-note">
                        Bằng cách tiếp tục, bạn đồng ý với
                        <a href="#">Điều khoản dịch vụ</a> và
                        <a href="#">Chính sách bảo mật</a>.
                    </p>
                </div>
            </div>`;
        }

        function showError(msg) {
            if (!overlay) return;
            const box = overlay.querySelector('#auth-error-box');
            const txt = overlay.querySelector('#auth-error-text');
            if (!msg) { box.style.display = 'none'; return; }
            txt.textContent = msg;
            box.style.display = 'flex';
        }

        function setLoading(btn, loading) {
            submitting = loading;
            overlay?.querySelectorAll('button, input').forEach(element => { element.disabled = loading; });
            if (loading) {
                btn.disabled = true;
                btn.innerHTML = '<span class="auth-spinner"></span> Đang xử lý...';
            } else {
                btn.disabled = false;
                const isLogin = mode === 'login';
                btn.innerHTML = `<i class="fas ${isLogin ? 'fa-sign-in-alt' : 'fa-user-plus'}"></i> ${isLogin ? 'Đăng nhập' : 'Tạo tài khoản'}`;
            }
        }

        function render() {
            const values = {};
            for (const id of ['auth-email', 'auth-pass', 'auth-name']) values[id] = overlay?.querySelector('#' + id)?.value || '';
            if (!overlay) {
                overlay = document.createElement('div');
                overlay.className = 'auth-overlay';
                document.body.appendChild(overlay);
                document.body.style.overflow = 'hidden';
            }
            overlay.innerHTML = buildHTML();
            for (const [id, value] of Object.entries(values)) {
                const input = overlay.querySelector('#' + id);
                if (input) input.value = value;
            }
            // Auto-focus first relevant input
            const firstInput = overlay.querySelector('#auth-name, #auth-email');
            if (firstInput) setTimeout(() => firstInput.focus(), 50);
        }

        async function handleSubmit() {
            if (submitting || !overlay) return;
            const email  = (overlay.querySelector('#auth-email')?.value || '').trim();
            const pass   = overlay.querySelector('#auth-pass')?.value || '';
            const name   = (overlay.querySelector('#auth-name')?.value || '').trim();
            const btn    = overlay.querySelector('#auth-submit-btn');

            showError('');
            if (!/^\S+@\S+\.\S+$/.test(email)) { showError('Email không hợp lệ.'); return; }
            if (pass.length < 6) { showError('Mật khẩu tối thiểu 6 ký tự.'); return; }
            if (mode === 'register' && !name) { showError('Vui lòng nhập tên hiển thị.'); return; }

            setLoading(btn, true);
            try {
                if (mode === 'login') await login(email, pass);
                else await register(email, pass, name);
                submitting = false;
                close();
                onSuccess?.();
            } catch (e) {
                showError(e.message || 'Đã xảy ra lỗi. Vui lòng thử lại.');
                setLoading(btn, false);
            }
        }

        async function handleDemo() {
            if (submitting || !overlay) return;
            submitting = true;
            overlay.querySelectorAll('button, input').forEach(element => { element.disabled = true; });
            const btn = overlay.querySelector('[data-act="demo"]');
            if (btn) { btn.disabled = true; btn.innerHTML = '<span class="auth-spinner"></span> Đang kết nối...'; }
            try {
                await demoLogin();
                submitting = false;
                close();
                onSuccess?.();
            } catch (e) {
                showError(e.message || 'Không thể đăng nhập demo. Thử lại sau.');
                submitting = false;
                overlay?.querySelectorAll('button, input').forEach(element => { element.disabled = false; });
                if (btn) { btn.disabled = false; btn.innerHTML = '<i class="fas fa-flask"></i> Thử với tài khoản Demo'; }
            }
        }

        render();

        overlay.addEventListener('click', (e) => {
            const act = e.target.closest('[data-act]')?.dataset.act;
            if (!act || submitting) return;
            if (act === 'close' || act === 'close-bg') {
                close();
            } else if (act === 'tab-login' && mode !== 'login') {
                mode = 'login'; render();
            } else if (act === 'tab-register' && mode !== 'register') {
                mode = 'register'; render();
            } else if (act === 'submit') {
                handleSubmit();
            } else if (act === 'demo') {
                handleDemo();
            } else if (act === 'toggle-pw') {
                const pwInput = overlay.querySelector('#auth-pass');
                const eyeIcon = overlay.querySelector('#pw-eye-icon');
                if (pwInput) {
                    const isHidden = pwInput.type === 'password';
                    pwInput.type = isHidden ? 'text' : 'password';
                    if (eyeIcon) {
                        eyeIcon.className = isHidden ? 'fas fa-eye-slash' : 'fas fa-eye';
                    }
                }
            }
        });

        // Submit on Enter key
        overlay.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && e.target.matches('input')) { e.preventDefault(); handleSubmit(); }
            if (e.key === 'Escape') close();
        });
    }

    function renderUserMenu(user, logoutBtn) {
        if (!logoutBtn) return;
        logoutBtn.innerHTML = `
            <div class="user-menu">
                <span class="user-name"></span>
                <button class="btn btn-secondary btn-sm" data-act="logout"><i class="fas fa-sign-out-alt"></i> Đăng xuất</button>
            </div>
        `;
        logoutBtn.querySelector('.user-name').textContent = normalizeUser(user).displayName;
        logoutBtn.querySelector('[data-act="logout"]').addEventListener('click', logout);
    }

    function initAuthUI(loginBtnSelector, userMenuSelector) {
        loadSavedUser();
        const loginBtn = document.querySelector(loginBtnSelector);
        const userMenu = document.querySelector(userMenuSelector);

        if (loginBtn && loginBtn === userMenu) {
            function renderSharedAuth(user) {
                loginBtn.style.display = '';
                if (user && api.isLoggedIn()) {
                    renderUserMenu(user, userMenu);
                } else {
                    loginBtn.innerHTML = '<button type="button" class="btn btn-primary btn-sm" data-act="login"><i class="fas fa-sign-in-alt"></i> Đăng nhập</button>';
                    loginBtn.querySelector('[data-act="login"]').addEventListener('click', () => renderAuthModal());
                }
            }
            renderSharedAuth(isLoggedIn() ? currentUser : null);
            onAuthChange(renderSharedAuth);
            return;
        }

        if (isLoggedIn()) {
            if (userMenu) renderUserMenu(currentUser, userMenu);
            if (loginBtn) loginBtn.style.display = 'none';
        } else {
            if (loginBtn) {
                loginBtn.style.display = '';
                loginBtn.addEventListener('click', () => renderAuthModal());
            }
        }

        onAuthChange((user) => {
            if (user) {
                if (userMenu) renderUserMenu(user, userMenu);
                if (loginBtn) loginBtn.style.display = 'none';
            } else {
                if (userMenu) userMenu.innerHTML = '';
                if (loginBtn) loginBtn.style.display = '';
            }
        });
    }

    window.MindSprintAuth = {
        login,
        demoLogin,
        register,
        logout,
        isLoggedIn,
        getCurrentUser,
        onAuthChange,
        initAuthUI,
        renderAuthModal
    };

    // Khởi tạo user từ localStorage khi load trang
    loadSavedUser();
    if (currentUser && !api.isLoggedIn()) {
        // Token hết hạn, xóa user
        currentUser = null;
        localStorage.removeItem(AUTH_STORAGE_KEY);
    }
    api.onSessionEnded(() => saveUser(null));
    window.MindSprintAuth.ready = api.isLoggedIn()
        ? api.getMe().then(async user => {
            if (JSON.stringify(normalizeUser(user)) !== JSON.stringify(currentUser)) await saveUser(user);
            return currentUser;
        }).catch(error => {
            if (error.status !== 401) console.warn('Chưa thể kiểm tra phiên đăng nhập:', error.message);
            return currentUser;
        })
        : Promise.resolve(null);
})();
