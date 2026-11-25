(function () {
    'use strict';
    var codeInput = document.getElementById('login-code');
    if (codeInput) {
        var updateKeyboard = function () {
            var method = document.querySelector('input[name="method"]:checked');
            codeInput.inputMode = method && method.value === 'recovery' ? 'text' : 'numeric';
        };
        document.querySelectorAll('input[name="method"]').forEach(function (radio) {
            radio.addEventListener('change', updateKeyboard);
        });
        updateKeyboard();
    }
    var qrBox = document.getElementById('authenticator-qr');
    if (qrBox) {
        try {
            var qr = qrcode(0, 'M');
            qr.addData(qrBox.dataset.authenticatorUri);
            qr.make();
            qrBox.innerHTML = qr.createSvgTag({ scalable: true, cellSize: 8, margin: 16 });
        } catch (_) { document.getElementById('qr-error').hidden = false; }
    }
    var codes = document.getElementById('recovery-codes');
    if (!codes) return;
    document.getElementById('copy-recovery').addEventListener('click', async function () {
        try {
            await navigator.clipboard.writeText(codes.textContent);
            document.getElementById('recovery-message').textContent = 'Đã sao chép mã dự phòng.';
        } catch (_) {
            document.getElementById('recovery-message').textContent = 'Không sao chép tự động được. Hãy chọn mã và sao chép hoặc tải file.';
        }
    });
    document.getElementById('download-recovery').addEventListener('click', function () {
        var url = URL.createObjectURL(new Blob([document.body.dataset.brand + ' - Mã dự phòng (mỗi mã dùng một lần)\n' + codes.textContent], { type: 'text/plain;charset=utf-8' }));
        var link = document.createElement('a');
        link.href = url; link.download = document.body.dataset.brand + '-ma-du-phong.txt';
        document.body.appendChild(link); link.click(); link.remove();
        setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
    });
}());
