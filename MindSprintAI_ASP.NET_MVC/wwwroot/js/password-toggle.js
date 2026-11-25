// Nút con mắt trong ô mật khẩu: hiện/ẩn chữ đã gõ (trang Đăng nhập).
document.querySelectorAll('[data-toggle-password]').forEach(button => {
    const input = button.parentElement.querySelector('input');
    button.addEventListener('click', () => {
        const show = input.type === 'password';
        input.type = show ? 'text' : 'password';
        button.setAttribute('aria-pressed', String(show));
        button.setAttribute('aria-label', show ? 'Ẩn mật khẩu' : 'Hiện mật khẩu');
    });
});
