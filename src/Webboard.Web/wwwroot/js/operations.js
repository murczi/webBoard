(() => {
    let busy = false;
    const result = document.getElementById('operation-result');
    document.querySelectorAll('.operation-form').forEach(form => form.addEventListener('submit', async event => {
        event.preventDefault();
        if (busy || !confirm(form.dataset.confirm)) return;
        busy = true;
        const buttons = [...document.querySelectorAll('.operation-form button')];
        buttons.forEach(button => button.disabled = true);
        result.textContent = 'Operation running…';
        try {
            const response = await fetch(form.action, { method: 'POST', body: new FormData(form), headers: { Accept: 'application/json' } });
            if (!response.ok) throw new Error(response.status === 403 ? 'Operation permission denied.' : 'Request failed. Verify target state before retrying.');
            const data = await response.json();
            result.textContent = data.message + (data.exitCode != null ? '\nExit code: ' + data.exitCode : '') + (data.output ? '\n' + data.output : '');
            form.querySelector('[name=requestId]').value = crypto.randomUUID();
        } catch (error) { result.textContent = error.message; }
        finally { busy = false; buttons.forEach(button => button.disabled = false); }
    }));
})();
