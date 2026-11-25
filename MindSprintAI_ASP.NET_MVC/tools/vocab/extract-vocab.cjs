// Chạy: node tools/vocab/extract-vocab.cjs ../../MindSprint-AI-D-n/frontend Data/Vocab/vocab.json ../../MindSprint-AI-D-n/backend/MindSprint.Api/Data/vocab_seed.json
// Đọc vocab_data.js + defaultFlashcards trong script.js của nhóm (chỉ đọc) → một tệp JSON gọn.
// Tham số 3 (không bắt buộc): bộ từ vựng phía backend của nhóm (DataSeeder) — thêm thẻ chưa có (trùng danh mục + câu hỏi thì bỏ).
const fs = require('fs'), vm = require('vm');
const [, , dir, out, seed] = process.argv;
const ctx = {};
vm.runInNewContext(fs.readFileSync(dir + '/vocab_data.js', 'utf8') + '\nthis.OXFORD = OXFORD_VOCAB_DATA;', ctx);
const script = fs.readFileSync(dir + '/script.js', 'utf8');
const m = script.match(/const defaultFlashcards = (\[[\s\S]*?\n    \]);/);
const defaults = m ? vm.runInNewContext('(' + m[1] + ')') : [];
const names = { english: 'Tiếng Anh', programming: 'Lập trình', general: 'Kiến thức chung' };
const card = c => ({ c: c.category, s: String(c.subCategory || 'Cơ bản').trim(), q: String(c.question || '').trim(), a: String(c.answer || '').trim(), e: c.example ? String(c.example).trim() : null });
const cards = [...defaults, ...ctx.OXFORD].filter(c => names[c.category] && c.question && c.answer).map(card);
if (seed) {
  const key = c => c.c + '|' + c.q.toLowerCase();
  const have = new Set(cards.map(key));
  for (const c of JSON.parse(fs.readFileSync(seed, 'utf8')).map(card)) {
    if (!names[c.c] || !c.q || !c.a || have.has(key(c))) continue;
    have.add(key(c)); cards.push(c);
  }
}
fs.writeFileSync(out, JSON.stringify({ source: 'MindSprint-AI-D-n frontend/vocab_data.js (3000 từ Oxford theo chủ đề, ZIM Academy) + defaultFlashcards trong script.js'
  + (seed ? ' + backend/MindSprint.Api/Data/vocab_seed.json' : ''), categories: names, cards }));
const by = {}; cards.forEach(c => by[c.c] = (by[c.c] || 0) + 1);
console.log('defaults', defaults.length, 'total', cards.length, JSON.stringify(by), 'bytes', fs.statSync(out).size);
