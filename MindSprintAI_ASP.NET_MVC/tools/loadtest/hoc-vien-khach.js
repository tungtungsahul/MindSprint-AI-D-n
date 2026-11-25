// Thử tải MindSprint AI: nhiều học viên KHÁCH (chỉ nhập tên, không mật khẩu) vào bài bằng mã và làm bài như trình duyệt thật.
// Chạy:  k6 run -e CODE=12345678 -e MODE=test -e VUS=650 tools/loadtest/hoc-vien-khach.js
//   CODE  mã vào bài (8 số) của bài giao
//   MODE  test = Kiểm tra (chờ GV bấm Bắt đầu, thời gian nghĩ 20–60 giây/câu) | practice = Ôn tập (5–20 giây/câu)
//   VUS   số học viên ảo; RAMP số giây để tất cả vào bài (mặc định 120); BASE địa chỉ web (mặc định https://mindsprint.example)
// Mỗi học viên: mở trang vào bài -> nhập mã + tên + tick đồng ý -> (Kiểm tra: chờ, hỏi trạng thái 30 giây/lần)
// -> Bắt đầu -> với từng câu: mở câu, nghĩ (gửi tín hiệu giám sát 15 giây/lần nếu bài bật), chọn 1 đáp án -> Nộp bài.
import http from 'k6/http';
import { check, sleep } from 'k6';
import { parseHTML } from 'k6/html';
import { Counter, Trend } from 'k6/metrics';

// Mã chống gửi trùng (clientRequestId) như crypto.randomUUID() của trình duyệt; tự viết để không tải thư viện ngoài.
const uuidv4 = () => 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
    const r = Math.random() * 16 | 0; return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
});

const BASE = __ENV.BASE || 'https://mindsprint.example';
const CODE = __ENV.CODE;
const MODE = __ENV.MODE || 'test';
const VUS = Number(__ENV.VUS || 650);
const RAMP = Number(__ENV.RAMP || 120);
// THINK=min,max (giây) để chạy thử nhanh, ví dụ -e THINK=1,2
const THINK = __ENV.THINK ? __ENV.THINK.split(',').map(Number) : MODE === 'test' ? [20, 60] : [5, 20];

const joined = new Counter('vao_bai_thanh_cong');
const started = new Counter('bat_dau_bai');
const answered = new Counter('cau_da_tra_loi');
const finished = new Counter('nop_bai_thanh_cong');
const rejected = new Counter('bi_tu_choi_429');
const answerTime = new Trend('thoi_gian_luu_cau_tra_loi', true);

export const options = {
    scenarios: { hoc_vien: { executor: 'per-vu-iterations', vus: VUS, iterations: 1, maxDuration: '45m' } },
    thresholds: {
        http_req_failed: ['rate<0.01'],                       // dưới 1% yêu cầu lỗi
        thoi_gian_luu_cau_tra_loi: ['p(95)<1000'],            // 95% lượt lưu câu trả lời dưới 1 giây
    },
    summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

const rand = (a, b) => a + Math.random() * (b - a);
const token = html => parseHTML(html).find('input[name="__RequestVerificationToken"]').attr('value');
function note(res) { if (res.status === 429) rejected.add(1); return res; }
function jsonPost(url, body, tok, name) {
    return note(http.post(url, JSON.stringify(body), {
        headers: { 'Content-Type': 'application/json', Accept: name === 'mo_cau' ? 'text/html' : 'application/json', RequestVerificationToken: tok },
        tags: { name },
    }));
}

export default function () {
    if (!CODE) throw new Error('Thiếu -e CODE=<mã vào bài>');
    sleep(Math.random() * RAMP);                              // học viên vào rải rác trong RAMP giây đầu

    // 1. Vào bài bằng mã
    let res = http.get(`${BASE}/Classrooms/Join?code=${CODE}`, { tags: { name: 'trang_vao_bai' } });
    if (!check(res, { 'mở trang vào bài': r => r.status === 200 })) return;
    res = note(http.post(`${BASE}/Classrooms/Join`, {
        __RequestVerificationToken: token(res.body), code: CODE,
        displayName: `Khách thử ${String(__VU).padStart(3, '0')}`, acceptPolicyShown: 'true', acceptPolicy: 'true',
    }, { tags: { name: 'vao_bai' } }));
    const m = /\/Learn\/Assignment\/(\d+)/.exec(res.url);
    if (!check(res, { 'vào bài thành công': r => r.status === 200 && m != null })) return;
    joined.add(1);
    const id = m[1];

    // 2. Kiểm tra: chờ GV bấm Bắt đầu (trang chờ hỏi trạng thái 30 giây/lần như test-waiting.js)
    while (res.body.includes('id="test-waiting"')) {
        sleep(rand(25, 30));
        const st = http.get(`${BASE}/Learn/TestStatus/${id}`, { headers: { Accept: 'application/json' }, tags: { name: 'hoi_trang_thai' } });
        if (st.status === 200 && st.json('canStart')) res = http.get(`${BASE}/Learn/Assignment/${id}`, { tags: { name: 'trang_bai' } });
    }

    // 3. Bắt đầu (đồng ý giám sát nếu bài bật)
    res = note(http.post(`${BASE}/Learn/Start/${id}`, { __RequestVerificationToken: token(res.body), id, consent: 'true', signals: '' },
        { tags: { name: 'bat_dau' } }));
    const play = /\/Learn\/Play\/(\d+)/.exec(res.url);
    if (__ENV.DEBUG) console.log(`Bắt đầu -> ${res.status} ${res.url} | ${(/<title>([^<]*)/.exec(res.body) || [])[1]} | câu: ${(res.body.match(/class="question card/g) || []).length}`);
    if (!check(res, { 'vào trang làm bài': r => r.status === 200 && play != null })) return;
    started.add(1);
    const attempt = play[1];
    const doc = parseHTML(res.body);
    const tok = token(res.body);
    const screenUrl = doc.find('#play').attr('data-screen-url');
    const questions = [];
    doc.find('section.question').each((_, s) => {
        const sec = parseHTML(s.innerHTML());
        questions.push({
            id: Number(s.attributes()['data-id'].value),
            version: Number(sec.find('[data-question-version]').attr('data-question-version')),
            options: sec.find('[data-option]').toArray().map(o => Number(o.attr('data-option'))),
        });
    });

    // 4. Làm từng câu
    for (const q of questions) {
        jsonPost(`${BASE}/Learn/OpenQuestion/${attempt}`, { attemptQuestionId: q.id, retry: false, questionVersion: q.version }, tok, 'mo_cau');
        let think = rand(THINK[0], THINK[1]);
        while (think > 0) {                                   // tín hiệu "vẫn đang làm" 15 giây/lần như screen-monitor.js
            const step = Math.min(15, think); sleep(step); think -= step;
            if (screenUrl) note(http.post(`${BASE}${screenUrl}`, { __RequestVerificationToken: tok, eventType: 'end', clientAt: new Date().toISOString() },
                { headers: { Accept: 'application/json' }, tags: { name: 'tin_hieu_giam_sat' } }));
        }
        const pick = q.options[Math.floor(Math.random() * q.options.length)];
        const r = jsonPost(`${BASE}/Learn/Answer/${attempt}`,
            { attemptQuestionId: q.id, clientRequestId: uuidv4(), selected: [pick], questionVersion: q.version }, tok, 'luu_cau_tra_loi');
        answerTime.add(r.timings.duration);
        if (check(r, { 'lưu câu trả lời': x => x.status === 200 })) answered.add(1);
    }

    // 5. Nộp bài
    res = note(http.post(`${BASE}/Learn/Finish/${attempt}`, { __RequestVerificationToken: tok }, { tags: { name: 'nop_bai' } }));
    if (check(res, { 'nộp bài thành công': r => r.status === 200 && r.url.includes('/Learn/Review/') })) finished.add(1);
}
