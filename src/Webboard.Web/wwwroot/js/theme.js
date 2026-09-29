(() => {
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    let choice = 'system';
    try { choice = localStorage.getItem('webboard-theme') || 'system'; } catch {}
    if (!['light', 'dark', 'system'].includes(choice)) choice = 'system';
    const apply = () => document.documentElement.setAttribute('data-bs-theme',
        choice === 'system' ? (media.matches ? 'dark' : 'light') : choice);
    apply();
    media.addEventListener('change', apply);
    document.addEventListener('DOMContentLoaded', () => {
        const select = document.getElementById('theme-choice');
        if (!select) return;
        select.value = choice;
        select.addEventListener('change', () => {
            choice = select.value;
            try { localStorage.setItem('webboard-theme', choice); } catch {}
            apply();
        });
    });
})();
