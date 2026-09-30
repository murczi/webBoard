(() => {
    const root = document.getElementById('monitoring-history');
    if (!root) return;
    const key = 'webboard.history-hidden.v1.' + root.dataset.account;
    const feedback = document.getElementById('history-feedback');
    const detail = document.getElementById('history-detail');
    const preset = document.getElementById('history-preset');
    const fromInput = document.getElementById('history-from');
    const untilInput = document.getElementById('history-until');
    const labels = ['Not configured', 'Healthy', 'Unhealthy', 'Unknown / missing'];
    const colors = ['#a67710', '#18764c', '#b42334', '#737b85'];
    let hidden = new Set();
    let storageMessage = '';
    try { const saved = JSON.parse(localStorage.getItem(key) || '[]'); if (Array.isArray(saved)) hidden = new Set(saved.filter(x => /^\d+$/.test(x))); }
    catch { storageMessage = 'Visibility preferences could not be loaded. '; }
    let rolling = true, from = Date.now() - 86400000, until = Date.now(), busy = false;
    let controller = new AbortController();
    const rows = [...root.querySelectorAll('[data-history-module]')];
    const visible = new Set();
    const localValue = ms => { const date = new Date(ms); return new Date(ms - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16); };
    const announce = text => feedback.textContent = storageMessage + text;
    function axis() {
        fromInput.value = localValue(from); untilInput.value = localValue(until);
        const axis = document.getElementById('history-axis'); axis.replaceChildren();
        for (let i = 0; i <= 4; i++) { const tick = document.createElement('span'); tick.textContent = new Date(from + (until - from) * i / 4).toLocaleString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }); axis.append(tick); }
    }
    document.getElementById('history-timezone').textContent = 'Times shown in ' + Intl.DateTimeFormat().resolvedOptions().timeZone;
    function save() { try { localStorage.setItem(key, JSON.stringify([...hidden])); } catch { storageMessage = 'Visibility could not be saved in this browser. '; announce(''); } }
    function setVisibility() {
        rows.forEach(row => row.hidden = hidden.has(row.dataset.historyModule));
        root.querySelectorAll('[data-history-toggle]').forEach(input => input.checked = !hidden.has(input.dataset.historyToggle));
    }
    function reset() {
        controller.abort(); controller = new AbortController();
        rows.forEach(row => { row.range = null; row.loaded = null; }); axis();
        void refresh();
    }
    preset.addEventListener('change', () => {
        rolling = preset.value !== 'custom';
        if (rolling) { until = Date.now(); from = until - Number(preset.value) * 3600000; reset(); }
    });
    [fromInput, untilInput].forEach(input => input.addEventListener('input', () => { rolling = false; preset.value = 'custom'; }));
    document.getElementById('history-range').addEventListener('submit', event => {
        event.preventDefault(); const a = new Date(fromInput.value).getTime(), b = new Date(untilInput.value).getTime();
        if (!Number.isFinite(a) || !Number.isFinite(b) || a >= b || b - a > 3660 * 86400000) { announce('Choose a valid range of at most ten years.'); return; }
        from = a; until = b; reset();
    });
    function navigate(direction) { const duration = until - from; from += direction * duration; until += direction * duration; rolling = false; preset.value = 'custom'; reset(); }
    document.getElementById('history-back').onclick = () => navigate(-1);
    document.getElementById('history-forward').onclick = () => navigate(1);
    root.querySelectorAll('[data-history-toggle]').forEach(input => input.onchange = () => {
        if (input.checked) hidden.delete(input.dataset.historyToggle); else hidden.add(input.dataset.historyToggle);
        setVisibility(); save(); void refresh();
    });
    document.getElementById('history-show').onclick = () => { hidden.clear(); setVisibility(); save(); void refresh(); };
    document.getElementById('history-hide').onclick = () => { rows.forEach(row => hidden.add(row.dataset.historyModule)); setVisibility(); save(); };
    async function get(handler, parameters, signal) {
        const response = await fetch('/MonitoringHistory?' + new URLSearchParams({ handler, ...parameters }), { cache: 'no-store', signal, headers: { Accept: 'application/json' } });
        if (!response.ok || response.redirected) throw new Error('History refresh failed. Existing display may be stale.');
        return response.json();
    }
    function setup(row) {
        const canvas = document.createElement('canvas'); canvas.style.width = '100%'; canvas.style.height = '28px'; canvas.tabIndex = 0;
        canvas.setAttribute('role', 'img'); canvas.setAttribute('aria-label', row.dataset.name + ' availability. Use left and right arrows to select a time.');
        row.querySelector('.history-track').append(canvas); row.canvas = canvas;
        let selected = .5, timer;
        async function inspect(fraction) {
            selected = Math.max(0, Math.min(1, fraction));
            try {
                const data = await get('Block', { moduleId: row.dataset.historyModule, at: new Date(from + (until - from) * selected).toISOString() }, controller.signal);
                const start = Math.max(from, new Date(data.start).getTime()), end = Math.min(until, new Date(data.end).getTime());
                const text = `${row.dataset.name} · ${labels[data.state]} · ${new Date(start).toLocaleString()} – ${new Date(end).toLocaleString()} · ${Math.max(0, (end - start) / 60_000).toFixed(1)} minutes${data.message ? ' · ' + data.message : ''}`;
                detail.textContent = text; canvas.title = text;
            } catch (error) { if (error.name !== 'AbortError') announce(error.message); }
        }
        canvas.onclick = event => void inspect((event.clientX - canvas.getBoundingClientRect().left) / canvas.clientWidth);
        canvas.onmousemove = event => { clearTimeout(timer); timer = setTimeout(() => void inspect((event.clientX - canvas.getBoundingClientRect().left) / canvas.clientWidth), 250); };
        canvas.onkeydown = event => { if (['ArrowLeft', 'ArrowRight', 'Enter', ' '].includes(event.key)) { event.preventDefault(); void inspect(selected + (event.key === 'ArrowLeft' ? -.01 : event.key === 'ArrowRight' ? .01 : 0)); } };
    }
    function prepare(row, range) {
        const canvas = row.canvas, width = Math.max(1, Math.round(canvas.clientWidth * devicePixelRatio));
        const same = row.range && row.range[0] === range[0] && row.range[1] === range[1] && canvas.width === width;
        if (same) return;
        const previous = document.createElement('canvas'); previous.width = canvas.width; previous.height = canvas.height;
        previous.getContext('2d').drawImage(canvas, 0, 0);
        canvas.width = width; canvas.height = Math.round(28 * devicePixelRatio);
        const context = canvas.getContext('2d'); context.fillStyle = colors[3]; context.fillRect(0, 0, width, canvas.height);
        if (row.range && row.loaded) {
            const scale = width / (range[1] - range[0]);
            context.drawImage(previous, (row.range[0] - range[0]) * scale, 0, (row.range[1] - row.range[0]) * scale, canvas.height);
        }
        row.range = range;
    }
    function draw(row, blocks, range) {
        const canvas = row.canvas, context = canvas.getContext('2d'), scale = canvas.width / (range[1] - range[0]);
        for (const block of blocks) {
            const x = (new Date(block.start).getTime() - range[0]) * scale, width = (new Date(block.end) - new Date(block.start)) * scale;
            context.fillStyle = colors[block.state]; context.fillRect(x, 0, width, canvas.height);
            if (block.state === 3) { context.save(); context.beginPath(); context.rect(x, 0, width, canvas.height); context.clip(); context.strokeStyle = '#626a73'; for (let line = x - canvas.height; line < x + width; line += 8 * devicePixelRatio) { context.beginPath(); context.moveTo(line, canvas.height); context.lineTo(line + canvas.height, 0); context.stroke(); } context.restore(); }
        }
    }
    async function load(row, range, signal) {
        prepare(row, range);
        // Revisit the freshness tail so expiry and newly collected observations are represented.
        let cursor = row.loaded ? Math.max(range[0], row.loaded - Number(root.dataset.staleSeconds) * 1000) : range[0];

        if (row.loaded && range[1] < Date.now() - Number(root.dataset.staleSeconds) * 1000) return;
        while (cursor < range[1]) {
            const data = await get('Data', { moduleId: row.dataset.historyModule, from: new Date(cursor).toISOString(), until: new Date(range[1]).toISOString() }, signal);
            if (signal.aborted) return;
            draw(row, data.blocks, range);
            if (row.dataset.enabled !== 'true' && data.blocks.length) row.querySelector('.history-current').textContent = 'Disabled · ' + labels[data.blocks.at(-1).state];
            if (!data.next) break;
            const next = new Date(data.next).getTime(); if (next <= cursor) break; cursor = next;
        }
        row.loaded = Math.min(range[1], Date.now());
    }
    async function refresh() {
        if (document.hidden || busy) return;
        busy = true;
        const signal = controller.signal;
        try {
            if (rolling) { until = Date.now(); from = until - Number(preset.value) * 3600000; axis(); }
            const range = [from, until];
            const queue = rows.filter(row => !row.hidden && visible.has(row));
            await Promise.all(Array.from({ length: Math.min(4, queue.length) }, async () => { while (queue.length && !signal.aborted) await load(queue.shift(), range, signal); }));
            const statuses = await get('Latest', {}, signal);
            statuses.forEach(status => { const row = rows.find(row => row.dataset.historyModule === String(status.moduleId)); if (row) row.querySelector('.history-current').textContent = labels[status.health.state]; });
            announce('');
        } catch (error) { if (error.name !== 'AbortError') announce(error.message); }
        finally { busy = false; if (signal !== controller.signal) void refresh(); }
    }
    rows.forEach(setup);
    const observer = new IntersectionObserver(entries => { entries.forEach(entry => { if (entry.isIntersecting) visible.add(entry.target); else visible.delete(entry.target); }); void refresh(); }, { rootMargin: '100px' });
    rows.forEach(row => observer.observe(row));
    setVisibility(); axis();
    setInterval(refresh, 30000);
    document.addEventListener('visibilitychange', () => { if (document.hidden) controller.abort(); else { controller = new AbortController(); void refresh(); } });
    window.addEventListener('resize', () => { rows.forEach(row => row.loaded = null); void refresh(); });
})();
