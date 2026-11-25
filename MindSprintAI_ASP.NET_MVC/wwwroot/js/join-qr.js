// QR chỉ chứa link do máy chủ tạo; thư viện vendor tự mã hóa SVG.
(function () {
    'use strict';
    var page = document.querySelector('[data-join-url]');
    if (!page) return;
    try {
        var qr = qrcode(0, 'M');
        qr.addData(page.dataset.joinUrl);
        qr.make();
        document.getElementById('join-qr').innerHTML = qr.createSvgTag({
            scalable: true, cellSize: 8, margin: 32,
            alt: 'Mã QR tham gia bằng mã ' + page.dataset.code
        });
    } catch (_) {
        document.getElementById('qr-error').hidden = false;
    }
}());
