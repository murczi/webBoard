document.querySelectorAll('form[data-confirm-delete]').forEach(form => {
    form.addEventListener('submit', event => {
        if (!confirm('Delete “' + form.dataset.confirmDelete + '”? This item will be removed from Webboard.')) event.preventDefault();
    });
});
const auditResource = document.querySelector('.audit-filters #Resource');
auditResource?.addEventListener('change', () => {
    const target = document.querySelector('input[name="TargetId"]');
    if (target) {
        target.value = '';
        target.nextElementSibling?.remove();
    }
});
// Keep action menus outside the clipping area of horizontally scrolling tables.
document.querySelectorAll('.table-responsive [data-bs-toggle="dropdown"], .user-table-responsive [data-bs-toggle="dropdown"]').forEach(button => {
    bootstrap.Dropdown.getOrCreateInstance(button, {
        popperConfig: defaults => ({ ...defaults, strategy: 'fixed' })
    });
});

document.querySelectorAll('time[data-local-date]').forEach(time => {
    const date = new Date(time.dateTime);
    if (!Number.isNaN(date.getTime())) time.textContent = date.toLocaleDateString();
});
