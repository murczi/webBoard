(() => {
    const modalElement = document.getElementById('audit-log-modal');
    if (!modalElement) return;

    const modal = bootstrap.Modal.getOrCreateInstance(modalElement);
    const targetLabel = document.getElementById('audit-log-target');
    const loading = document.getElementById('audit-log-loading');
    const error = document.getElementById('audit-log-error');
    const empty = document.getElementById('audit-log-empty');
    const results = document.getElementById('audit-log-results');
    const rows = document.getElementById('audit-log-rows');
    const pagination = document.getElementById('audit-log-pagination');
    const pageLabel = document.getElementById('audit-log-page-label');
    const previous = document.getElementById('audit-log-previous');
    const next = document.getElementById('audit-log-next');
    let endpoint = '';
    let currentPage = 1;
    let totalPages = 1;

    const setVisible = (element, visible) => element.classList.toggle('d-none', !visible);

    const loadPage = async page => {
        setVisible(loading, true);
        setVisible(error, false);
        setVisible(empty, false);
        setVisible(results, false);
        setVisible(pagination, false);

        try {
            const url = new URL(endpoint, window.location.origin);
            url.searchParams.set('page', page);
            const response = await fetch(url, { headers: { Accept: 'application/json' } });
            if (!response.ok) throw new Error(`Request failed with status ${response.status}`);

            const data = await response.json();
            rows.replaceChildren();
            data.items.forEach(log => {
                const row = document.createElement('tr');
                const actor = document.createElement('td');
                const comment = document.createElement('td');
                const dateCreated = document.createElement('td');
                actor.className = 'text-muted';
                dateCreated.className = 'text-muted text-nowrap';
                actor.textContent = log.actorName;
                comment.textContent = log.comment;
                dateCreated.textContent = new Intl.DateTimeFormat(undefined, {
                    dateStyle: 'medium',
                    timeStyle: 'short'
                }).format(new Date(log.dateCreated));
                row.append(actor, comment, dateCreated);
                rows.appendChild(row);
            });

            currentPage = data.page;
            totalPages = data.totalPages;
            setVisible(empty, data.items.length === 0);
            setVisible(results, data.items.length > 0);
            setVisible(pagination, data.totalItems > 0);
            pageLabel.textContent = `Page ${currentPage} of ${totalPages} · ${data.totalItems} logs`;
            previous.disabled = currentPage <= 1;
            next.disabled = currentPage >= totalPages;
        }
        catch {
            setVisible(error, true);
        }
        finally {
            setVisible(loading, false);
        }
    };

    document.querySelectorAll('.js-view-logs').forEach(button => {
        button.addEventListener('click', () => {
            endpoint = button.dataset.logsUrl;
            targetLabel.textContent = button.dataset.logsTarget;
            modal.show();
            loadPage(1);
        });
    });

    previous.addEventListener('click', () => loadPage(currentPage - 1));
    next.addEventListener('click', () => loadPage(currentPage + 1));
})();
