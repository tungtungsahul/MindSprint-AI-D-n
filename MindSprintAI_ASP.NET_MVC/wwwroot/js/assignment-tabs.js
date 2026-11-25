(() => {
    'use strict';
    const tabs = document.querySelector('.b-tabs');
    if (tabs && typeof bootstrap !== 'undefined') {
        const openHash = () => {
            const key = ['#cau-hoi', '#chong-gian-lan', '#cai-dat', '#cham-lai'].includes(location.hash) ? location.hash.slice(1) : 'theo-doi';
            bootstrap.Tab.getOrCreateInstance(document.getElementById(`tab-${key}`)).show();
        };
        tabs.addEventListener('shown.bs.tab', event => {
            const key = event.target.id.replace('tab-', '');
            history.replaceState(null, '', location.pathname + location.search + (key === 'theo-doi' ? '' : `#${key}`));
        });
        openHash();
        window.addEventListener('hashchange', openHash);
        // The shared regrade partial is outside this page's edit scope.
        document.querySelectorAll('#pane-cham-lai form').forEach(form => {
            form.action = form.action.split('#')[0] + '#cham-lai';
        });
    }
    const small = document.getElementById('assignment-qr');
    const modal = document.getElementById('assignment-qr-modal');
    if (!small || !modal) return;
    const fallback = document.getElementById('assignment-qr-fallback');
    const preview = small.closest('.b-qr-preview');
    const draw = (target, cellSize) => {
        const qr = qrcode(0, 'M');
        qr.addData(small.dataset.joinUrl);
        qr.make();
        target.innerHTML = qr.createSvgTag({ scalable: true, cellSize, margin: cellSize * 4,
            alt: { text: 'Mã QR tham gia bằng mã ' + small.dataset.code, id: target.id + '-description' } });
    };
    try {
        draw(small, 4);
        preview.hidden = false;
    } catch {
        preview.hidden = true;
        if (fallback) fallback.hidden = false;
    }
    // QR lớn trong popup do js/qr-popup.js vẽ.
    modal.addEventListener('hidden.bs.modal', () => setTimeout(() => small.focus(), 0));
})();
