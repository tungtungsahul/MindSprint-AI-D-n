// MindSprint API client - nối frontend (HTML/CSS/JS) với backend ASP.NET Core.
// Đổi API_BASE thành URL backend khi deploy (Render/SmarterASP).
(function () {
    const DEFAULT_API_BASE = 'http://localhost:5000';
    const API_BASE = localStorage.getItem('api-base') || DEFAULT_API_BASE;
    const TOKEN_KEY = 'ms-token';
    const REFRESH_TOKEN_KEY = 'ms-refresh-token';

    const getToken = () => localStorage.getItem(TOKEN_KEY);
    const getRefreshToken = () => localStorage.getItem(REFRESH_TOKEN_KEY);
    const setTokens = (access, refresh) => {
        localStorage.setItem(TOKEN_KEY, access);
        localStorage.setItem(REFRESH_TOKEN_KEY, refresh);
    };
    const clearTokens = () => {
        localStorage.removeItem(TOKEN_KEY);
        localStorage.removeItem(REFRESH_TOKEN_KEY);
    };

    let isRefreshing = false;
    let pendingRequests = [];

    async function request(path, { method = 'GET', body, auth = true, retry = true } = {}) {
        const headers = { 'Content-Type': 'application/json' };
        if (auth && getToken()) headers.Authorization = 'Bearer ' + getToken();
        const res = await fetch(API_BASE + path, { method, headers, body: body ? JSON.stringify(body) : undefined });

        if (res.status === 401 && auth && retry && getRefreshToken()) {
            // Try to refresh token
            const newToken = await refreshAccessToken();
            if (newToken) {
                // Retry original request with new token
                return request(path, { method, body, auth, retry: false });
            }
            // Refresh failed, clear tokens and throw
            clearTokens();
            throw new Error('Phiên đăng nhập hết hạn. Vui lòng đăng nhập lại.');
        }

        if (res.status === 401 && auth) {
            clearTokens();
            throw new Error('Phiên đăng nhập hết hạn. Vui lòng đăng nhập lại.');
        }

        if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            throw new Error(err.message || 'Lỗi ' + res.status);
        }
        return res.status === 204 ? null : res.json();
    }

    async function refreshAccessToken() {
        if (isRefreshing) {
            // Wait for ongoing refresh
            return new Promise(resolve => {
                pendingRequests.push(resolve);
            });
        }

        isRefreshing = true;
        const refreshToken = getRefreshToken();
        if (!refreshToken) {
            isRefreshing = false;
            return null;
        }

        try {
            const res = await fetch(API_BASE + '/api/auth/refresh', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ refreshToken, deviceHint: navigator.userAgent })
            });

            if (!res.ok) {
                return null;
            }

            const data = await res.json();
            setTokens(data.token, data.refreshToken);

            // Resolve all pending requests
            pendingRequests.forEach(resolve => resolve(data.token));
            pendingRequests = [];
            return data.token;
        } catch {
            return null;
        } finally {
            isRefreshing = false;
        }
    }

    async function authenticate(path, payload) {
        const data = await request(path, { method: 'POST', body: payload, auth: false });
        setTokens(data.token, data.refreshToken);
        return data.user;
    }

    async function upload(path, formData) {
        const res = await fetch(API_BASE + path, { method: 'POST', headers: { Authorization: 'Bearer ' + getToken() }, body: formData });
        if (res.status === 401) {
            const newToken = await refreshAccessToken();
            if (newToken) {
                const retryRes = await fetch(API_BASE + path, { method: 'POST', headers: { Authorization: 'Bearer ' + newToken }, body: formData });
                if (retryRes.ok) return retryRes.json();
            }
            clearTokens();
            throw new Error('Phiên đăng nhập hết hạn.');
        }
        if (!res.ok) { const err = await res.json().catch(() => ({})); throw new Error(err.message || 'Lỗi ' + res.status); }
        return res.json();
    }

    window.MindSprintApi = {
        isLoggedIn: () => !!getToken(),
        login: (email, password) => authenticate('/api/auth/login', { email, password }),
        register: (email, password, displayName) => authenticate('/api/auth/register', { email, password, displayName }),
        logout: () => clearTokens(),
        revokeCurrentSession: () => request('/api/auth/revoke', { method: 'POST', body: { refreshToken: getRefreshToken() } }),
        revokeAllSessions: () => request('/api/auth/revoke-all', { method: 'POST' }),

        // Flashcards + trạng thái SRS
        getCards: () => request('/api/flashcards'),
        createCard: (c) => request('/api/flashcards', { method: 'POST', body: { externalId: String(c.id), category: c.category, subCategory: c.subCategory || null, question: c.question, answer: c.answer, example: c.example || null } }),
        updateCard: (c) => request('/api/flashcards/' + encodeURIComponent(c.id), { method: 'PUT', body: { category: c.category, subCategory: c.subCategory || null, question: c.question, answer: c.answer, example: c.example || null } }),
        deleteCard: (id) => request('/api/flashcards/' + encodeURIComponent(id), { method: 'DELETE' }),

        // Spaced Repetition: gửi kết quả ôn, server tính NextReviewDate
        review: (cardId, remembered) => request('/api/review', { method: 'POST', body: { cardId: String(cardId), remembered } }),
        getDue: () => request('/api/review/due'),

        // Kanban
        getTasks: () => request('/api/tasks'),
        createTask: (t) => request('/api/tasks', { method: 'POST', body: t }),
        moveTask: (id, status, position) => request(`/api/tasks/${id}/move`, { method: 'PATCH', body: { status, position } }),
        deleteTask: (id) => request('/api/tasks/' + id, { method: 'DELETE' }),

        // Notebook (kiểu NotebookLM)
        nbList: () => request('/api/notebooks'),
        nbCreate: (title) => request('/api/notebooks', { method: 'POST', body: { title } }),
        nbDelete: (id) => request('/api/notebooks/' + id, { method: 'DELETE' }),
        nbSources: (id) => request(`/api/notebooks/${id}/sources`),
        nbAddText: (id, title, text) => request(`/api/notebooks/${id}/sources/text`, { method: 'POST', body: { title, text } }),
        nbAddUrl: (id, url) => request(`/api/notebooks/${id}/sources/url`, { method: 'POST', body: { url } }),
        nbAddFile: (id, file) => { const f = new FormData(); f.append('file', file); return upload(`/api/notebooks/${id}/sources/file`, f); },
        nbDeleteSource: (id, sid) => request(`/api/notebooks/${id}/sources/${sid}`, { method: 'DELETE' }),
        nbChat: (id, question, history) => request(`/api/notebooks/${id}/chat`, { method: 'POST', body: { question, history } }),
        nbSuggestions: (id) => request(`/api/notebooks/${id}/suggestions`),
        nbGenerate: (id, type, focus, count) => request(`/api/notebooks/${id}/generate`, { method: 'POST', body: { type, focus, count } }),
        nbSaveCards: (id, cards) => request(`/api/notebooks/${id}/save-cards`, { method: 'POST', body: { cards } }),
        nbNotes: (id) => request(`/api/notebooks/${id}/notes`),
        nbAddNote: (id, title, content) => request(`/api/notebooks/${id}/notes`, { method: 'POST', body: { title, content } }),
        nbDeleteNote: (id, nid) => request(`/api/notebooks/${id}/notes/${nid}`, { method: 'DELETE' }),

        // Gemini
        aiFlashcards: (text, count = 10) => request('/api/ai/flashcards', { method: 'POST', body: { text, count } }),
        aiQuiz: (text, count = 10) => request('/api/ai/quiz', { method: 'POST', body: { text, count } })
    };
})();