const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

test('late permission response cannot overwrite another user or enable saving after close', async () => {
    let source = fs.readFileSync(path.join(__dirname, '../../src/Webboard.Web/Pages/UserManagement.cshtml'), 'utf8').split('<script>')[1].split('</script>')[0];
    source = source.replace(/@Json\.Serialize\([^)]*\)/g, 'null');
    const start = source.indexOf('const renderAccess = access =>');
    const end = source.indexOf("document.querySelectorAll('.js-edit-access')", start);
    source = source.slice(0, start) + 'const renderAccess = access => { globalThis.displayed = access; };\n' + source.slice(end);
    const nodes = new Map();
    const element = id => {
        if (!nodes.has(id)) nodes.set(id, { value: '', events: {}, classList: { add() {}, remove() {} }, addEventListener(name, callback) { this.events[name] = callback; } });
        return nodes.get(id);
    };
    const buttons = ['A', 'B', 'C'].map(id => ({ dataset: { userId: id, userName: id, accessUrl: id }, addEventListener(name, callback) { this.click = callback; } }));
    const resolve = {};
    const context = vm.createContext({ AbortController, window: {}, document: { getElementById: element, querySelectorAll: () => buttons }, bootstrap: { Modal: { getOrCreateInstance: () => ({ show() {} }) } }, fetch: id => new Promise(r => resolve[id] = () => r({ ok: true, json: async () => id + ' permissions' })) });
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../../src/Webboard.Web/wwwroot/js/request-lifecycle.js'), 'utf8'), context);
    context.Webboard = context.window.Webboard;
    vm.runInContext(source, context);
    const a = buttons[0].click(), b = buttons[1].click();
    resolve.B(); await b; resolve.A(); await a;
    assert.equal(element('access-user-id').value, 'B');
    assert.equal(context.displayed, 'B permissions');
    const c = buttons[2].click();
    element('access-modal').events['hide.bs.modal']();
    resolve.C(); await c;
    assert.equal(context.displayed, 'B permissions');
    assert.equal(element('access-save').disabled, true);
});
