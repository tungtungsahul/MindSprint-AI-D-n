// Run: node tests/MindSprintAI_ASP.NET_MVC.Tests/gradebook-pager.check.cjs
// Exercise the shipped script against replaceable DOM nodes, without a browser or dependencies.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

class Element {
    constructor(text = '') { this.text = text; this.children = []; this.dataset = {}; this.attributes = {}; this.events = {}; this.classes = new Set(); this.hidden = false; }
    get textContent() { return this.text + this.children.map(child => child.textContent).join(''); }
    set textContent(value) { this.text = value; this.children = []; }
    get classList() { return { contains: name => this.classes.has(name) }; }
    get rows() { return this.children; }
    append(child) {
        if (child.parentElement) child.parentElement.children.splice(child.parentElement.children.indexOf(child), 1);
        this.children.push(child); child.parentElement = this;
    }
    replaceChildren(...children) { this.children = []; this.text = ''; children.forEach(child => this.append(child)); }
    setAttribute(key, value) { this.attributes[key] = value; }
    addEventListener(key, callback) { this.events[key] = callback; }
    trigger(key) { this.events[key]?.(); }
    querySelector() { return this.device ? new Element() : null; }
}
const controls = Object.fromEntries(['assignment-live', 'gradebook-tools', 'gradebook-pages', 'gradebook-search', 'gradebook-size', 'gradebook-attention'].map(id => [id, new Element()]));
const [root, tools, pages, search, size, attention] = Object.values(controls);
search.value = ''; size.value = '10';
const groups = [new Element(), new Element()];
tools.querySelectorAll = () => groups;
function table(count, outsider = false) {
    const body = new Element();
    for (let i = 1; i <= count; i++) {
        const name = outsider ? 'Khách' : i === 53 ? 'Đặng Ánh' : 'Học viên ' + i;
        const row = new Element(name); row.id = i;
        row.cells = [{ firstChild: { textContent: name } }];
        if (i % 11 === 0) row.classes.add('table-warning');
        row.device = i === 7;
        body.append(row);
    }
    const result = new Element(); result.tBodies = [body]; result.parentElement = new Element();
    return result;
}
let tables = [table(53), table(1, true)];
root.querySelectorAll = selector => selector === '.b-grade-table' ? tables : [];
let blockedStorage = false;
const saved = new Map();
const document = {
    activeElement: search,
    getElementById: id => controls[id],
    createElement: () => new Element(), createTextNode: text => new Element(text),
    dispatchEvent() {}
};
let observer;
class Observer {
    constructor(callback) { this.callback = callback; observer = this; }
    observe() {} disconnect() {}
}
const context = {
    document, MutationObserver: Observer, CustomEvent: class {},
    localStorage: {
        getItem(key) { if (blockedStorage) throw Error('blocked'); return saved.get(key); },
        setItem(key, value) { if (blockedStorage) throw Error('blocked'); saved.set(key, value); }
    }
};
vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../wwwroot/js/gradebook-pager.js'), 'utf8'), context);
const visible = () => tables[0].tBodies[0].rows.filter(row => !row.hidden);
const summary = () => pages.children.at(-1).textContent;
assert.equal(visible().length, 10);
assert.equal(attention.textContent, 'Cần chú ý (5)');
assert.deepEqual(visible().slice(0, 5).map(row => row.id), [7, 11, 22, 33, 44]);
assert.match(summary(), /1–10 trên 53/);
search.value = 'dang anh'; search.trigger('input');
assert.equal(visible().length, 1); assert.equal(visible()[0].id, 53);
assert.match(summary(), /1–1 trên 1/);
search.value = ''; search.trigger('input'); groups[1].trigger('click');
assert.equal(tables[0].parentElement.hidden, true);
assert.equal(tables[1].parentElement.hidden, false);
assert.match(summary(), /trên 1 học viên/);
groups[0].trigger('click');
pages.children[0].children.find(node => node.textContent === '2').trigger('click');
assert.match(summary(), /11–20 trên 53/);
tables = [table(53), table(1, true)]; observer.callback();
assert.match(summary(), /11–20 trên 53/);
assert.equal(document.activeElement, search);
search.value = 'Đặng'; search.trigger('input');
tables = [table(53), table(1, true)]; observer.callback();
assert.equal(search.value, 'Đặng'); assert.equal(visible()[0].id, 53);
search.value = ''; search.trigger('input');
size.value = '25'; size.trigger('change');
assert.equal(visible().length, 25); assert.equal(saved.get('mindsprint-gradebook-size'), '25');
size.value = 'all'; size.trigger('change'); assert.equal(visible().length, 53);
attention.trigger('click'); assert.equal(visible().length, 5);
attention.trigger('click'); assert.equal(visible().length, 53);
blockedStorage = true; size.value = '10'; size.trigger('change');
assert.equal(visible().length, 10);
console.log('gradebook-pager: 21 behavior checks passed (paging, diacritics, alerts, groups, refresh, focus, storage failure).');
