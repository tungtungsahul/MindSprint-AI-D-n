// Shared Auth Module - dùng chung cho toàn app
// Xử lý đăng nhập, đăng ký, đăng xuất, kiểm tra trạng thái đăng nhập
(function () {
    const api = window.MindSprintApi;
    if (!api) return;

    let currentUser = null;
    let authListeners = [];
    const AUTH_STORAGE_KEY = 'ms-user';

    function notifyAuthChange() {
        authListeners.forEach(fn => fn(currentUser));
    }

    function saveUser(user) {
        currentUser = user;
        if (user) {
            localStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(user));
        } else {
            localStorage.removeItem(AUTH_STORAGE_KEY);
        }
        notifyAuthChange();
    }

    function loadSavedUser() {
        const saved = localStorage.getItem(AUTH_STORAGE_KEY);
        if (saved) {
            try {
                currentUser = JSON.parse(saved);
            } catch {
                currentUser = null;
            }
        }
    }

    async function login(email, password) {
        const user = await api.login(email, password);
        saveUser(user);
        return user;
    }

    async function register(email, password, displayName) {
        const user = await api.register(email, password, displayName);
        saveUser(user);
        return user;
    }

    function logout() {
        api.logout();
        saveUser(null);
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
        let mode = 'login';
        let container = null;

        function render() {
            if (!container) {
                container = document.createElement('div');
                container.className = 'auth-modal-overlay';
                document.body.appendChild(container);
            }
            container.innerHTML = `
                <div class="auth-modal glass-panel">
                    <button class="auth-close" data-act="close">&times;</button>
                    <h3>${mode === 'login' ? 'Đăng nhập' : 'Tạo tài khoản'}</h3>
                    ${mode === 'register' ? '<input id="auth-name" type="text" placeholder="Tên hiển thị" maxlength="60">' : ''}
                    <input id="auth-email" type="email" placeholder="Email" autocomplete="email">
                    <input id="auth-pass" type="password" placeholder="Mật khẩu (tối thiểu 6 ký tự)" autocomplete="${mode === 'login' ? 'current-password' : 'new-password'}">
                    <div class="auth-err" id="auth-err"></div>
                    <button class="btn btn-primary" data-act="submit">${mode === 'login' ? 'Đăng nhập' : 'Đăng ký'}</button>
                    <a href="#" data-act="toggle" style="font-size:.85rem;color:var(--primary-color);margin-top:.5rem;display:block;text-align:center">
                        ${mode === 'login' ? 'Chưa có tài khoản? Đăng ký' : 'Đã có tài khoản? Đăng nhập'}
                    </a>
                </div>
            `;
        }

        async function handleSubmit(btn) {
            const email = container.querySelector('#auth-email').value.trim();
            const pass = container.querySelector('#auth-pass').value;
            const name = container.querySelector('#auth-name')?.value?.trim();
            const errEl = container.querySelector('#auth-err');

            if (!/^\S+@\S+\.\S+$/.test(email)) { errEl.textContent = 'Email không hợp lệ.'; return; }
            if (pass.length < 6) { errEl.textContent = 'Mật khẩu tối thiểu 6 ký tự.'; return; }
            if (mode === 'register' && !name) { errEl.textContent = 'Vui lòng nhập tên hiển thị.'; return; }

            btn.disabled = true;
            btn.style.opacity = 0.6;
            try {
                if (mode === 'login') await login(email, pass);
                else await register(email, pass, name);
                container.remove();
                container = null;
                onSuccess?.();
            } catch (e) {
                errEl.textContent = e.message;
            } finally {
                btn.disabled = false;
                btn.style.opacity = 1;
            }
        }

        container.addEventListener('click', (e) => {
            const act = e.target.dataset.act;
            if (act === 'close') { container.remove(); container = null; }
            else if (act === 'toggle') { e.preventDefault(); mode = mode === 'login' ? 'register' : 'login'; render(); }
            else if (act === 'submit') { handleSubmit(e.target); }
        });

        render();
    }

    function renderUserMenu(user, logoutBtn) {
        if (!logoutBtn) return;
        logoutBtn.innerHTML = `
            <div class="user-menu">
                <span class="user-name">${user.DisplayName || user.Email}</span>
                <button class="btn btn-secondary btn-sm" data-act="logout"><i class="fas fa-sign-out-alt"></i> Đăng xuất</button>
            </div>
        `;
        logoutBtn.querySelector('[data-act="logout"]').addEventListener('click', () => {
            logout();
            logoutBtn.innerHTML = '<button class="btn btn-primary btn-sm" data-act="login"><i class="fas fa-sign-in-alt"></i> Đăng nhập</button>';
            logoutBtn.querySelector('[data-act="login"]').addEventListener('click', () => renderAuthModal());
        });
    }

    function initAuthUI(loginBtnSelector, userMenuSelector) {
        loadSavedUser();
        const loginBtn = document.querySelector(loginBtnSelector);
        const userMenu = document.querySelector(userMenuSelector);

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
})();