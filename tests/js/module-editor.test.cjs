const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');

// Run the real editor with a small DOM substitute, including its Docker settings.
function fixture(items, fail = false) {
    class Option {
        constructor(text, value) { this.text = text; this.value = value; }
    }
    const elements = new Map();
    const element = key => {
        if (!elements.has(key)) elements.set(key, {
            value: key === 'Module_HostId' ? '1' : '', options: [],
            add(option) { this.options.push(option); },
            replaceChildren(...options) { this.options = options; this.value = options[0]?.value || ''; },
            addEventListener() {}, setAttribute() {}, removeAttribute() {}
        });
        return elements.get(key);
    };
    const document = {
        getElementById: element,
        querySelector: () => null,
        querySelectorAll: () => []
    };
    const form = {
        dataset: { containersUrl: '/containers', servicesUrl: '/services' },
        closest: () => element('modal'),
        querySelector: selector => element(selector.match(/name="([^"]+)"/)[1]),
        addEventListener() {}
    };
    const context = vm.createContext({
        document, Option, URL, AbortController,
        window: { location: { href: 'https://dashboard.test/' }, Webboard: { SearchableSelect: { refresh() {} } } },
        fetch: async () => {
            if (fail) throw new Error('Agent unavailable');
            return { ok: true, json: async () => items };
        }
    });
    const source = readFileSync(resolve(__dirname, '../../src/Webboard.Web/wwwroot/js/module-editor.js'), 'utf8');
    // Expose the class only in this isolated test context; skip page bootstrapping.
    vm.runInContext(source.replace("const form = document.getElementById('module-form');", 'globalThis.Editor = ModuleEditor; const form = null;'), context);
    const editor = new context.Editor(form, {});
    return editor.loaders.container;
}
const container = (id, name = 'amp-server') => ({ id, name, image: 'amp:latest', status: 'Up' });

test('Docker selections keep their name when recreation changes the ID', async () => {
    const first = fixture([container('old-id')]);
    await first.load();
    assert.equal(first.select.options[1].value, 'amp-server');
    const recreated = fixture([container('new-id')]);
    await recreated.load('amp-server');
    assert.equal(recreated.select.value, 'amp-server');
    assert.equal(recreated.select.options.length, 2);
});

test('editing a discoverable legacy ID selects its name for the next save', async () => {
    const loader = fixture([container('old-id')]);
    await loader.load('old-id');
    assert.equal(loader.select.value, 'amp-server');
    assert.equal(loader.select.options.length, 2);
});

test('missing containers retain the saved name and recover on rediscovery', async () => {
    const loader = fixture([]);
    await loader.load('amp-server');
    assert.equal(loader.select.value, 'amp-server');
    assert.match(loader.select.options[1].text, /not currently found/);
    const recovered = fixture([container('replacement-id')]);
    await recovered.load(loader.select.value);
    assert.equal(recovered.select.value, 'amp-server');
    assert.equal(recovered.select.options.length, 2);
});

test('removed legacy IDs are retained without guessing a replacement', async () => {
    const loader = fixture([container('new-id')]);
    await loader.load('old-id');
    assert.equal(loader.select.value, 'old-id');
    assert.equal(loader.select.options.length, 3);
});

test('an exact container name takes precedence over a legacy ID match', async () => {
    const loader = fixture([container('first-id', 'abc123'), container('abc123', 'other')]);
    await loader.load('abc123');
    assert.equal(loader.select.value, 'abc123');
});

test('discovery failure retains the saved name', async () => {
    const loader = fixture([], true);
    await loader.load('amp-server');
    assert.equal(loader.select.value, 'amp-server');
    assert.match(loader.select.options[0].text, /unable to refresh/);
});
