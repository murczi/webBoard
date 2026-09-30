(() => {
    // The original select remains the source of truth for Razor binding and validation.
    class SearchableSelect {
        static instances = new WeakMap();

        static refresh(select) {
            SearchableSelect.instances.get(select)?.refresh();
        }

        constructor(select) {
            this.select = select;
            this.activeIndex = -1;
            this.wrapper = document.createElement('div');
            this.wrapper.className = 'searchable-select';
            this.input = document.createElement('input');
            this.input.type = 'text';
            this.input.className = 'form-select searchable-select-input';
            this.input.id = `${select.id}-search`;
            this.input.autocomplete = 'off';
            this.input.spellcheck = false;
            this.input.setAttribute('role', 'combobox');
            this.input.setAttribute('aria-autocomplete', 'list');
            this.input.setAttribute('aria-haspopup', 'listbox');
            this.input.setAttribute('aria-expanded', 'false');
            this.list = document.createElement('div');
            this.list.id = `${select.id}-options`;
            this.list.className = 'searchable-select-options';
            this.list.setAttribute('role', 'listbox');
            this.list.hidden = true;
            this.input.setAttribute('aria-controls', this.list.id);
            this.status = document.createElement('div');
            this.status.className = 'visually-hidden';
            this.status.setAttribute('role', 'status');
            this.status.setAttribute('aria-live', 'polite');
            const labels = [...select.labels];
            this.list.setAttribute('aria-label', labels.map(label => label.textContent.trim()).join(' ') || 'Options');
            for (const label of labels) label.htmlFor = this.input.id;
            const description = select.getAttribute('aria-describedby');
            if (description) this.input.setAttribute('aria-describedby', description);
            this.wrapper.append(this.input, this.list, this.status);
            select.after(this.wrapper);
            select.hidden = true;
            SearchableSelect.instances.set(select, this);
            this.input.addEventListener('focus', () => this.input.select());
            this.input.addEventListener('click', () => this.open());
            this.input.addEventListener('input', () => {
                if (!this.isOpen) this.open(false);
                this.render();
            });
            this.input.addEventListener('keydown', event => this.onKeyDown(event));
            this.input.addEventListener('blur', () => this.close());
            this.list.addEventListener('pointerdown', event => event.preventDefault());
            this.list.addEventListener('click', event => {
                const option = event.target.closest('[data-option-index]');
                if (option) this.choose(Number(option.dataset.optionIndex));
            });
            select.addEventListener('change', () => this.refresh());
            select.addEventListener('invalid', event => {
                event.preventDefault();
                this.input.setAttribute('aria-invalid', 'true');
                this.input.focus();
                this.input.setCustomValidity(select.validationMessage);
                this.input.reportValidity();
            });
            select.form?.addEventListener('reset', () => queueMicrotask(() => this.refresh()));
            select.closest('.modal')?.addEventListener('hide.bs.modal', () => this.close());
            new MutationObserver(() => this.refresh()).observe(select, {
                childList: true, subtree: true, attributes: true,
                attributeFilter: ['disabled', 'required', 'selected', 'label', 'aria-busy']
            });
            this.refresh();
        }

        get isOpen() { return !this.list.hidden; }

        refresh() {
            this.input.disabled = this.select.disabled;
            this.input.setAttribute('aria-required', String(this.select.required));
            this.input.setAttribute('aria-busy', String(this.select.getAttribute('aria-busy') === 'true'));
            this.input.setCustomValidity('');
            this.input.removeAttribute('aria-invalid');
            if (this.select.disabled || !this.isOpen) this.close();
            else this.render();
        }

        open(clear = true) {
            if (this.select.disabled || this.isOpen) return;
            this.list.hidden = false;
            this.input.setAttribute('aria-expanded', 'true');
            this.wrapper.classList.add('is-open');
            this.input.placeholder = this.select.dataset.searchPlaceholder || 'Search options…';
            if (clear) this.input.value = '';
            this.render();
        }

        close() {
            this.list.hidden = true;
            this.input.setAttribute('aria-expanded', 'false');
            this.input.removeAttribute('aria-activedescendant');
            this.wrapper.classList.remove('is-open');
            this.input.value = this.select.selectedOptions[0]?.text || '';
            this.input.placeholder = this.select.dataset.searchPlaceholder || 'Select an option';
            this.activeIndex = -1;
        }

        render() {
            const query = this.input.value.trim().toLocaleLowerCase();
            this.matches = [...this.select.options].filter(option =>
                !option.disabled && !option.hidden && (!query || option.text.toLocaleLowerCase().includes(query)));
            this.list.replaceChildren();
            this.matches.forEach((option, index) => {
                const item = document.createElement('div');
                item.id = `${this.list.id}-${index}`;
                item.className = 'searchable-select-option';
                item.setAttribute('role', 'option');
                item.setAttribute('aria-selected', String(option.selected));
                item.dataset.optionIndex = index;
                item.textContent = option.text;
                this.list.append(item);
            });
            if (!this.matches.length) {
                const empty = document.createElement('div');
                empty.className = 'searchable-select-empty';
                empty.textContent = 'No matching options';
                this.list.append(empty);
            }
            this.status.textContent = `${this.matches.length} options found`;
            this.activeIndex = -1;
            this.input.removeAttribute('aria-activedescendant');
        }

        activate(index) {
            this.activeIndex = Math.max(0, Math.min(index, this.matches.length - 1));
            [...this.list.children].forEach((item, position) => item.classList.toggle('is-active', position === this.activeIndex));
            const item = this.matches.length ? this.list.children[this.activeIndex] : null;
            if (item) {
                this.input.setAttribute('aria-activedescendant', item.id);
                item.scrollIntoView({ block: 'nearest' });
            }
        }

        choose(index) {
            const option = this.matches[index];
            if (!option) return;
            this.select.value = option.value;
            this.input.setCustomValidity('');
            this.input.removeAttribute('aria-invalid');
            this.close();
            this.select.dispatchEvent(new Event('change', { bubbles: true }));
        }

        onKeyDown(event) {
            if (!this.isOpen && ['Enter', ' '].includes(event.key)) {
                event.preventDefault();
                this.open();
            } else if (['ArrowDown', 'ArrowUp'].includes(event.key)) {
                event.preventDefault();
                this.open();
                this.activate(this.activeIndex < 0
                    ? (event.key === 'ArrowDown' ? 0 : this.matches.length - 1)
                    : this.activeIndex + (event.key === 'ArrowDown' ? 1 : -1));
            } else if (this.isOpen && ['Home', 'End'].includes(event.key)) {
                event.preventDefault();
                this.activate(event.key === 'Home' ? 0 : this.matches.length - 1);
            } else if (this.isOpen && event.key === 'Enter') {
                event.preventDefault();
                if (this.activeIndex >= 0) this.choose(this.activeIndex);
                else if (this.matches.length === 1) this.choose(0);
            } else if (this.isOpen && event.key === 'Escape') {
                event.preventDefault();
                event.stopPropagation();
                this.close();
            } else if (event.key === 'Tab') this.close();
        }
    }

    window.Webboard = window.Webboard || {};
    window.Webboard.SearchableSelect = SearchableSelect;
    document.querySelectorAll('select[data-searchable-select]').forEach(select => new SearchableSelect(select));
})();
