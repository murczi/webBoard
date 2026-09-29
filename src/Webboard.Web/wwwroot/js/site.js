const auditResource = document.querySelector('.audit-filters #Resource');
auditResource?.addEventListener('change', () => {
    const target = document.querySelector('input[name="TargetId"]');
    if (target) {
        target.value = '';
        target.nextElementSibling?.remove();
    }
});
