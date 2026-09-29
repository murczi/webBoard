(() => {
    const grid = document.querySelector('[data-module-grid]');
    if (!grid || !/^[1-9]\d*$/.test(grid.dataset.orderAccount || '')) return;

    const key = 'webboard.module-order.v1.' + grid.dataset.orderAccount;
    const defaultCards = [...grid.querySelectorAll(':scope > [data-module-id]')];
    const defaultIds = defaultCards.map(card => card.dataset.moduleId);
    const visibleIds = new Set(defaultIds);
    const arranging = grid.dataset.arrange === 'true';
    const status = document.getElementById('arrangement-status');
    const announce = message => { if (status) status.textContent = message; };
    let sequence = [];
    let storageReadable = true;
    try {
        const stored = JSON.parse(localStorage.getItem(key) || '[]');
        if (Array.isArray(stored))
            sequence = [...new Set(stored.filter(id => typeof id === 'string' && /^[1-9]\d*$/.test(id)))];
    } catch {
        storageReadable = false;
    }
    // Keep unavailable IDs: they may belong to temporarily hidden modules.
    for (const id of defaultIds)
        if (!sequence.includes(id)) sequence.push(id);

    const cards = () => [...grid.querySelectorAll(':scope > [data-module-id]')];
    const updateControls = () => {
        const current = cards();
        current.forEach((card, index) => {
            const position = card.querySelector('.arrangement-position');
            if (position) position.textContent = (index + 1) + ' of ' + current.length;
            const earlier = card.querySelector('[data-move="-1"]');
            const later = card.querySelector('[data-move="1"]');
            if (earlier) earlier.disabled = index === 0;
            if (later) later.disabled = index === current.length - 1;
        });
    };
    const applySequence = () => {
        const byId = new Map(defaultCards.map(card => [card.dataset.moduleId, card]));
        for (const id of sequence) {
            const card = byId.get(id);
            if (card) grid.append(card);
        }
        updateControls();
    };
    applySequence();
    if (!arranging) return;
    if (!storageReadable) announce('Saved arrangement could not be read. You can still arrange tiles on this page.');

    const save = message => {
        const visibleOrder = cards().map(card => card.dataset.moduleId);
        let index = 0;
        // Replace visible slots only, preserving hidden modules at their saved positions.
        sequence = sequence.map(id => visibleIds.has(id) ? visibleOrder[index++] : id);
        try {
            localStorage.setItem(key, JSON.stringify(sequence));
            announce(message + ' Saved in this browser.');
        } catch {
            announce(message + ' Could not save in this browser; this arrangement lasts only for this page.');
        }
        updateControls();
    };
    const move = (card, target, after) => {
        if (!target || target === card) return;
        const before = cards().map(item => item.dataset.moduleId).join(',');
        grid.insertBefore(card, after ? target.nextSibling : target);
        if (before !== cards().map(item => item.dataset.moduleId).join(',')) {
            save(card.dataset.moduleName + ' moved to position ' + (cards().indexOf(card) + 1) + ' of ' + cards().length + '.');
        }
        card.querySelector('.drag-handle')?.focus({ preventScroll: true });
    };
    grid.addEventListener('click', event => {
        const button = event.target.closest('[data-move]');
        if (!button || button.disabled) return;
        const card = button.closest('[data-module-id]');
        const current = cards();
        const offset = Number(button.dataset.move);
        move(card, current[current.indexOf(card) + offset], offset > 0);
    });
    grid.addEventListener('keydown', event => {
        const handle = event.target.closest('.drag-handle');
        if (!handle || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
        event.preventDefault();
        const card = handle.closest('[data-module-id]');
        const offset = ['ArrowLeft', 'ArrowUp'].includes(event.key) ? -1 : 1;
        move(card, cards()[cards().indexOf(card) + offset], offset > 0);
    });

    document.querySelector('[data-reset-order]')?.addEventListener('click', () => {
        if (drag) finishDrag(false);
        defaultCards.forEach(card => grid.append(card));
        sequence = [...defaultIds];
        updateControls();
        try {
            localStorage.removeItem(key);
            announce('Alphabetical arrangement restored. Saved in this browser.');
        } catch {
            announce('Alphabetical arrangement restored for this page, but the saved preference could not be cleared.');
        }
    });

    let drag = null;
    let frame = 0;
    const clearMarker = () => grid.querySelectorAll('.drop-before, .drop-after').forEach(card => card.classList.remove('drop-before', 'drop-after'));
    const locateTarget = () => {
        clearMarker();
        const target = document.elementFromPoint(drag.x, drag.y)?.closest('[data-module-id]');
        drag.target = target && target.parentElement === grid && target !== drag.card ? target : null;
        if (drag.target) {
            const rect = drag.target.getBoundingClientRect();
            const singleColumn = getComputedStyle(grid).gridTemplateColumns.split(' ').length === 1;
            drag.after = singleColumn ? drag.y >= rect.top + rect.height / 2 : drag.x >= rect.left + rect.width / 2;
            drag.target.classList.add(drag.after ? 'drop-after' : 'drop-before');
        }
    };
    const tick = () => {
        if (!drag?.active) return;
        const edge = 65;
        const speed = drag.y < edge ? -12 : drag.y > innerHeight - edge ? 12 : 0;
        if (speed) window.scrollBy(0, speed);
        locateTarget();
        frame = requestAnimationFrame(tick);
    };
    const finishDrag = commit => {
        if (!drag) return;
        const completed = drag;
        drag = null;
        cancelAnimationFrame(frame);
        clearMarker();
        completed.card.classList.remove('is-dragging');
        completed.ghost?.remove();
        document.body.classList.remove('is-arranging');
        if (completed.handle.hasPointerCapture(completed.pointerId))
            completed.handle.releasePointerCapture(completed.pointerId);
        if (commit && completed.active && completed.target)
            move(completed.card, completed.target, completed.after);
        else if (completed.active)
            announce('Move cancelled. Arrangement unchanged.');
    };
    grid.addEventListener('pointerdown', event => {
        const handle = event.target.closest('.drag-handle');
        if (!handle || event.button !== 0 || drag) return;
        const card = handle.closest('[data-module-id]');
        drag = { card, handle, pointerId: event.pointerId, startX: event.clientX, startY: event.clientY,
            x: event.clientX, y: event.clientY, active: false, target: null };
        handle.setPointerCapture(event.pointerId);
    });
    grid.addEventListener('pointermove', event => {
        if (!drag || event.pointerId !== drag.pointerId) return;
        drag.x = event.clientX;
        drag.y = event.clientY;
        if (!drag.active && Math.hypot(drag.x - drag.startX, drag.y - drag.startY) < 6) return;
        event.preventDefault();
        if (!drag.active) {
            drag.active = true;
            drag.card.classList.add('is-dragging');
            document.body.classList.add('is-arranging');
            drag.ghost = document.createElement('div');
            drag.ghost.className = 'arrangement-drag-preview';
            drag.ghost.setAttribute('aria-hidden', 'true');
            drag.ghost.textContent = drag.card.dataset.moduleName;
            document.body.append(drag.ghost);
            frame = requestAnimationFrame(tick);
        }
        drag.ghost.style.left = (drag.x + 12) + 'px';
        drag.ghost.style.top = (drag.y + 12) + 'px';
        locateTarget();
    });
    grid.addEventListener('pointerup', event => { if (drag?.pointerId === event.pointerId) finishDrag(true); });
    grid.addEventListener('pointercancel', () => finishDrag(false));
    grid.addEventListener('lostpointercapture', () => finishDrag(false));
    document.addEventListener('keydown', event => { if (event.key === 'Escape' && drag) finishDrag(false); });
    window.addEventListener('blur', () => finishDrag(false));
})();
