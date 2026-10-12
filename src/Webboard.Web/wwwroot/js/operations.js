(() => {
    let busy = false;
    document.querySelectorAll('[data-capabilities-url]').forEach(menu => {
        const requests = new Webboard.LatestRequest();
        const actions = menu.querySelector('[data-operation-actions]');
        menu.addEventListener('hide.bs.dropdown', () => requests.cancel());
        menu.addEventListener('show.bs.dropdown', async () => {
            const request = requests.begin(menu.dataset.capabilitiesUrl);
            actions.textContent = 'Loading operations…';
            try {
                const response = await fetch(request.target, { signal: request.signal, cache: 'no-store' });
                if (!response.ok || response.redirected) throw new Error('Operations could not be loaded.');
                const html = await response.text();
                if (!requests.isCurrent(request, menu.dataset.capabilitiesUrl)) return;
                actions.innerHTML = html;
                actions.querySelectorAll('button').forEach(button => button.disabled = busy);
            } catch (error) {
                if (requests.isCurrent(request, menu.dataset.capabilitiesUrl)) actions.textContent = error.message;
            }
        });
    });
    document.addEventListener('submit', async event => {
        const form = event.target.closest('.operation-form');
        if (!form) return;
        event.preventDefault();
        if (busy || !confirm(form.dataset.confirm)) return;
        const result = form.closest('.module-tile').querySelector('.operation-result');
        bootstrap.Dropdown.getInstance(form.closest('.dropdown').querySelector('[data-bs-toggle="dropdown"]'))?.hide();
        busy = true;
        const buttons = [...document.querySelectorAll('.operation-form button')];
        buttons.forEach(button => button.disabled = true);
        result.hidden = false;
        result.textContent = 'Operation running…';
        try {
            const response = await fetch(form.action, { method: 'POST', body: new FormData(form), headers: { Accept: 'application/json' } });
            if (!response.ok || response.redirected) throw new Error(response.status === 403 ? 'Operation permission denied.' : 'Request failed. Verify target state before retrying.');
            const data = await response.json();
            result.textContent = data.message + (data.exitCode != null ? '\nExit code: ' + data.exitCode : '') + (data.output ? '\n' + data.output : '');
            form.querySelector('[name=requestId]').value = crypto.randomUUID();
        } catch (error) { result.textContent = error.message; }
        finally { busy = false; buttons.forEach(button => button.disabled = false); }
    });
})();
