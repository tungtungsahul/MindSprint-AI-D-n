// Tự gợi ý số thẻ/câu khi dán tài liệu (estimateOptimalCardCount của nhóm): ~380 ký tự một ý; từ khóa định nghĩa/công thức
// cộng thêm (tối đa 1,8 lần theo độ dài); kẹp 5–100. Người dùng đã tự sửa ô số lượng thì không ghi đè.
(() => {
    const estimate = text => {
        const clean = text.trim();
        if (!clean) return 10;
        const byLength = Math.round(clean.length / 380);
        const keys = clean.match(/(?:định nghĩa|công thức|tính chất|khái niệm|nguyên lý|đặc điểm|quy tắc|bước|lý thuyết|phương pháp|theorem|definition|formula|\$|£|≤|≥|=)/gi) ?? [];
        const count = Math.max(byLength, Math.min(Math.round(keys.length * 0.4), Math.round(byLength * 1.8)));
        return Math.max(5, Math.min(100, count));
    };
    document.querySelectorAll('input[data-estimate-from]').forEach(count => {
        const source = document.getElementById(count.dataset.estimateFrom);
        if (!source) return;
        let timer, edited = false;
        count.addEventListener('input', () => { edited = true; });
        source.addEventListener('input', () => {
            clearTimeout(timer);
            timer = setTimeout(() => { if (!edited && source.value.trim().length > 80) count.value = Math.min(estimate(source.value), Number(count.max) || 100); }, 300);
        });
    });
})();
