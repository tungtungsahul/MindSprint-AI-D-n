(() => {
    const fallback = image => { image.hidden = true; };
    document.addEventListener('error', event => {
        if (event.target.matches?.('.avatar-img')) fallback(event.target);
    }, true);
    // Ảnh trong bộ nhớ có thể lỗi trước khi đoạn mã này được nạp.
    document.querySelectorAll('.avatar-img').forEach(image => {
        if (image.complete && image.naturalWidth === 0) fallback(image);
    });
})();
