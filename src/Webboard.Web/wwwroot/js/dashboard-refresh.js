(() => {
    const grid = document.querySelector('[data-module-grid]');
    if (!grid) return;
    let active = false;
    const feedback = document.getElementById('dashboard-refresh-status');
    async function refresh() {
        if (document.hidden || active) return;
        active = true;
        try {
            const response = await fetch('/?handler=Status', { headers: { Accept: 'application/json' }, cache: 'no-store', signal: AbortSignal.timeout(10000) });
            if (!response.ok || response.redirected) throw new Error();
            const statuses = await response.json();
            const ids = new Set(statuses.map(status => String(status.moduleId)));
            grid.querySelectorAll('[data-module-id]').forEach(card => card.hidden = !ids.has(card.dataset.moduleId));
            for (const status of statuses) {
                const card = grid.querySelector(`[data-module-id="${status.moduleId}"]`);
                if (!card) { feedback.textContent = 'New modules are available. Reload to display them.'; continue; }
                card.querySelector('[data-health-message]').textContent = status.health.message;
                card.querySelector('[data-health-latency]').textContent = status.health.pingMilliseconds == null ? '' : `Response time: ${status.health.pingMilliseconds} ms`;
                card.querySelector('[data-health-time]').textContent = status.timestamp ? 'Last checked: ' + new Date(status.timestamp).toLocaleString() : 'Not checked yet';
                const steam = status.health.steam;
                card.querySelector('[data-steam-details]').hidden = !steam;
                if (steam) card.querySelector('[data-steam-info]').textContent = `${steam.game} · ${steam.version}\n${steam.name} · ${steam.map}\n${steam.players}/${steam.maxPlayers} players` +
                    (steam.playerDetails ? '\n' + steam.playerDetails.map(player => `${player.name}: ${player.score} (${Math.floor(player.durationSeconds / 60)} minutes)`).join('\n') : '');
                card.querySelector('.module-health-dot').className = 'module-health-dot module-health-dot--' + (status.health.state === 1 ? 'healthy' : status.health.state === 2 ? 'unhealthy' : 'unknown');
            }
            if (!statuses.some(status => !grid.querySelector(`[data-module-id="${status.moduleId}"]`))) feedback.textContent = '';
        } catch { feedback.textContent = 'Status refresh is unavailable. Displayed observations may be stale.'; }
        finally { active = false; }
    }
    setInterval(refresh, 15000);
    document.addEventListener('visibilitychange', refresh);
    refresh();
})();
