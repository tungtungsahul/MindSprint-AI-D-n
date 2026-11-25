// Popup mã QR (Views/Shared/_QrPopup.cshtml): vẽ QR lần đầu mở; thư viện lỗi thì vẫn còn mã số để học viên nhập.
document.querySelectorAll('.qr-popup').forEach(modal => {
    modal.addEventListener('show.bs.modal', () => {
        const box = modal.querySelector('.qr-popup-code');
        if (box.firstChild) return;
        try {
            const qr = qrcode(0, 'M');
            qr.addData(modal.dataset.joinUrl);
            qr.make();
            box.innerHTML = qr.createSvgTag({ scalable: true, cellSize: 8, margin: 32 });
        } catch {
            box.hidden = true;
            modal.querySelector('.qr-popup-error').hidden = false;
        }
    });
});
