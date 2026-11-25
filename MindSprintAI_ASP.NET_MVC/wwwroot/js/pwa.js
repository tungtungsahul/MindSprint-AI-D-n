// Đăng ký service worker (cài ứng dụng + trang báo mất mạng) và nút "Cài đặt ứng dụng" ở trang Cài đặt (như nhóm).
(() => {
    if ('serviceWorker' in navigator) window.addEventListener('load', () => navigator.serviceWorker.register('/sw.js').catch(() => { }));
    const button = document.getElementById('pwa-install');
    const status = document.getElementById('pwa-status');
    let prompt = null;
    if (window.matchMedia('(display-mode: standalone)').matches && status) status.textContent = 'MindSprint AI đã được cài trên máy này.';
    window.addEventListener('beforeinstallprompt', e => {
        e.preventDefault();
        prompt = e;
        if (button) button.hidden = false;
    });
    button?.addEventListener('click', async () => {
        if (!prompt) return;
        prompt.prompt();
        const choice = await prompt.userChoice;
        prompt = null;
        button.hidden = true;
        if (status) status.textContent = choice.outcome === 'accepted' ? 'Đã cài MindSprint AI lên máy.' : 'Bạn có thể cài lại bất cứ lúc nào.';
    });
    window.addEventListener('appinstalled', () => { if (status) status.textContent = 'Đã cài MindSprint AI lên máy.'; });
})();
