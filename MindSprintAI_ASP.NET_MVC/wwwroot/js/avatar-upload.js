// "Đổi ảnh đại diện" (ui.js của nhóm): cắt giữa ảnh thành hình vuông 256×256 rồi mới gửi, để ảnh nhẹ (máy chủ nhận tối đa 300 KB).
// Trình duyệt không thu nhỏ được thì vẫn gửi ảnh gốc; máy chủ báo nếu quá cỡ.
(() => {
    const form = document.getElementById('avatar-upload-form');
    const input = form?.querySelector('input[type=file]');
    if (!form || !input) return;
    input.addEventListener('change', async () => {
        const file = input.files[0];
        if (!file) return;
        try {
            const bitmap = await createImageBitmap(file);
            const side = Math.min(bitmap.width, bitmap.height);
            const canvas = document.createElement('canvas');
            canvas.width = canvas.height = 256;
            canvas.getContext('2d').drawImage(bitmap, (bitmap.width - side) / 2, (bitmap.height - side) / 2, side, side, 0, 0, 256, 256);
            const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.88));
            if (blob) {
                const files = new DataTransfer();
                files.items.add(new File([blob], 'avatar.jpg', { type: 'image/jpeg' }));
                input.files = files.files;
            }
        } catch { }
        form.requestSubmit();
    });
})();
