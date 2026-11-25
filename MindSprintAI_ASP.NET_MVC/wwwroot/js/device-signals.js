// (F7) module vanilla; thông số chỉ tồn tại trong bộ nhớ và yêu cầu gửi, không lưu ở trình duyệt.
const root = document.getElementById('machine-hint');
const light = () => ({
    screen: `${screen.width}x${screen.height}x${screen.colorDepth}`,
    timezone: Intl.DateTimeFormat().resolvedOptions().timeZone || 'unknown',
    language: navigator.language || 'unknown', platform: navigator.platform || 'unknown',
    cores: String(navigator.hardwareConcurrency || 'unknown'), memory: String(navigator.deviceMemory || 'unknown')
});
async function full() {
    const result = light();
    try {
        const canvas = document.createElement('canvas'); canvas.width = 160; canvas.height = 40;
        const ctx = canvas.getContext('2d');
        ctx.font = '16px Arial'; ctx.fillText('MindSprint AI học và nhớ 123', 2, 22);
        result.canvas = canvas.toDataURL();
        const gl = document.createElement('canvas').getContext('webgl');
        const ext = gl?.getExtension('WEBGL_debug_renderer_info');
        if (gl) result.webgl = String(gl.getParameter(ext?.UNMASKED_RENDERER_WEBGL || gl.RENDERER));
        const baseline = (font) => { ctx.font = `16px ${font}`; return ctx.measureText('mmmmmmlliWW').width; };
        const width = baseline('monospace');
        result.fonts = ['Arial', 'Times New Roman', 'Calibri', 'Verdana', 'Tahoma', 'Georgia', 'Courier New']
            .filter(font => baseline(`"${font}", monospace`) !== width).join(',') || 'unknown';
    } catch { /* (F9) chặn thu thập = chưa biết, không cản làm bài. */ }
    try {
        const audio = new OfflineAudioContext(1, 4096, 44100);
        const oscillator = audio.createOscillator(); oscillator.type = 'triangle'; oscillator.frequency.value = 1000;
        const compressor = audio.createDynamicsCompressor();
        oscillator.connect(compressor); compressor.connect(audio.destination); oscillator.start();
        const rendered = await Promise.race([audio.startRendering(), new Promise(resolve => setTimeout(() => resolve(null), 1000))]);
        if (rendered) result.audio = String(rendered.getChannelData(0).slice(3000).reduce((sum, n) => sum + Math.abs(n), 0));
    } catch { /* (F9) mức đầy đủ có thể không có. */ }
    return result;
}
const lightReady = (async () => {
    if (!root) return;
    try {
        const body = new FormData(root); body.set('signalsJson', JSON.stringify(light()));
        await fetch(root.action, { method: 'POST', body, credentials: 'same-origin', signal: AbortSignal.timeout(2000) });
    } catch { /* nhận diện không được chặn đăng nhập */ }
})();
// (F7b) chỉ bắt đầu mức đầy đủ sau thao tác đồng ý; Takeover chỉ gắn cờ khi lượt đã đồng ý.
document.addEventListener('submit', async event => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || form === root || form.dataset.machineReady === 'true') return;
    if (form.dataset.machineFull !== 'true' && form.dataset.machineLight !== 'true') return;
    if (form.method.toLowerCase() !== 'post') return;
    event.preventDefault();
    const button = event.submitter;
    await lightReady;
    // lightReady thường đã xong: không chờ sang lượt sau thì requestSubmit chạy khi sự kiện submit này chưa kết thúc và bị trình duyệt bỏ qua.
    await new Promise(resolve => setTimeout(resolve));
    if (form.dataset.machineFull === 'true') {
        try { form.elements.namedItem('signals').value = JSON.stringify(await full()); }
        catch { /* gửi thiếu thông số vẫn được */ }
    }
    form.dataset.machineReady = 'true';
    if (button instanceof HTMLButtonElement || button instanceof HTMLInputElement) form.requestSubmit(button);
    else form.requestSubmit();
}, true);
