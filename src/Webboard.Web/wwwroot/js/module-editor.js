(() => {
    const refreshSelect = select => window.Webboard.SearchableSelect.refresh(select);

    // All module types use the same field lifecycle, including disabled/required state.
    class ModuleTypeConfiguration {
        constructor(editor) {
            this.editor = editor;
            this.definitions = {
                docker: { host: true, remote: 'container' },
                systemd: { host: true, remote: 'service' },
                minecraft: { server: 'minecraft' },
                steam: { server: 'steam' }
            };
            this.groups = {
                health: ['HealthCheckUrl'],
                container: ['ContainerId'],
                service: ['ServiceName'],
                minecraft: ['MinecraftServerAddress', 'MinecraftServerPort'],
                steam: ['SteamServerAddress', 'SteamQueryPort', 'SteamQueryPlayers']
            };
        }

        apply() {
            const { editor } = this;
            const kind = editor.input('TypeId').selectedOptions[0]?.dataset.typeName?.toLowerCase();
            const definition = this.definitions[kind] || { host: true, health: true };
            this.current = definition;
            const host = editor.input('HostId');
            host.disabled = !definition.host;
            host.required = Boolean(definition.remote);
            if (host.options[0]?.value === '')
                host.options[0].text = definition.remote ? 'Select a host' : 'No host (optional)';
            if (!definition.host) host.value = '';
            editor.field('host').classList.toggle('d-none', !definition.host);
            editor.field('type').classList.toggle('col-md-6', Boolean(definition.host));
            editor.field('type').classList.toggle('col-12', !definition.host);
            for (const [group, names] of Object.entries(this.groups)) {
                const visible = group === 'health' ? Boolean(definition.health)
                    : definition.remote === group || definition.server === group;
                const field = editor.field(group === 'health' ? 'health-url' : group);
                field.classList.toggle('d-none', !visible);
                field.classList.add('module-type-fields');
                for (const name of names) {
                    const input = editor.input(name);
                    input.disabled = !visible;
                    input.required = visible && group !== 'health' && input.type !== 'checkbox';
                }
            }
            refreshSelect(host);
            for (const loader of Object.values(editor.loaders)) loader.cancel();
            for (const loader of Object.values(editor.loaders)) refreshSelect(loader.select);
            if (definition.remote) void editor.loaders[definition.remote].load(editor.input(
                definition.remote === 'container' ? 'ContainerId' : 'ServiceName').value);
            editor.updateSaveState();
        }
    }

    // Docker and systemd share fetching, cancellation, retained values, and feedback.
    class RemoteModuleOptions {
        constructor(editor, name, settings) {
            this.editor = editor;
            this.name = name;
            this.settings = settings;
            this.select = editor.input(settings.input);
            this.help = document.getElementById(`module-${name}-help`);
        }

        cancel() {
            this.request?.abort();
            this.request = null;
            this.loading = false;
            this.select.removeAttribute('aria-busy');
        }

        placeholder(message, value = '') {
            this.select.replaceChildren(new Option(message, value, true, true));
            refreshSelect(this.select);
        }

        async load(selected = '') {
            this.cancel();
            const hostId = this.editor.input('HostId').value;
            this.select.disabled = true;
            if (!hostId) {
                this.placeholder(`Select a host to load ${this.settings.plural}`);
                this.help.textContent = `A host with a ${this.settings.agent}-enabled agent is required.`;
                this.editor.updateSaveState();
                return;
            }
            const request = new AbortController();
            this.request = request;
            this.loading = true;
            this.select.setAttribute('aria-busy', 'true');
            this.placeholder(`Loading ${this.settings.plural}…`);
            this.help.textContent = `Loading ${this.settings.plural} live from the selected host…`;
            this.editor.updateSaveState();
            try {
                const url = new URL(this.settings.url, window.location.href);
                url.searchParams.set('hostId', hostId);
                const response = await fetch(url, { signal: request.signal });
                if (!response.ok) {
                    const problem = await response.json().catch(() => null);
                    throw new Error(problem?.error || `Unable to load ${this.settings.plural}.`);
                }
                const items = await response.json();
                if (this.request !== request) return;
                this.select.replaceChildren(new Option(`Select a ${this.name}`, ''));
                for (const item of items) this.select.add(new Option(this.settings.label(item), this.settings.value(item)));
                if (selected && !items.some(item => this.settings.value(item) === selected))
                    this.select.add(new Option(`Previously selected ${this.name} (not currently found)`, selected));
                this.select.value = selected;
                this.select.disabled = false;
                this.help.textContent = items.length ? `${this.settings.title} loaded live from the selected host.`
                    : `No ${this.settings.plural} were found on this host.`;
            } catch (error) {
                if (this.request !== request || error.name === 'AbortError') return;
                this.placeholder(selected ? `Previously selected ${this.name} (unable to refresh)`
                    : `Unable to load ${this.settings.plural}`, selected);
                this.select.disabled = false;
                this.help.textContent = `${error.message} Reselect the host to retry.`;
            } finally {
                if (this.request === request) {
                    this.loading = false;
                    this.select.removeAttribute('aria-busy');
                    refreshSelect(this.select);
                    this.editor.updateSaveState();
                }
            }
        }
    }

    class ModuleEditor {
        constructor(form, state) {
            this.form = form;
            this.modal = form.closest('.modal');
            this.save = document.getElementById('save-module');
            this.loaders = {
                container: new RemoteModuleOptions(this, 'container', {
                    input: 'ContainerId', url: form.dataset.containersUrl, plural: 'containers', title: 'Containers', agent: 'Docker',
                    value: item => item.id, label: item => `${item.name} — ${item.image} (${item.status})`
                }),
                service: new RemoteModuleOptions(this, 'service', {
                    input: 'ServiceName', url: form.dataset.servicesUrl, plural: 'services', title: 'Services', agent: 'systemd',
                    value: item => item.name, label: item => `${item.name} — ${item.description} (${item.activeState}/${item.subState})`
                })
            };
            this.configuration = new ModuleTypeConfiguration(this);
            document.querySelector('[data-module-action="add"]')?.addEventListener('click', () => this.open());
            document.querySelectorAll('.js-edit-module').forEach(button =>
                button.addEventListener('click', () => this.open(button.dataset)));
            this.input('TypeId').addEventListener('change', () => this.configuration.apply());
            this.input('HostId').addEventListener('change', () => {
                const remote = this.configuration.current.remote;
                if (remote) void this.loaders[remote].load();
            });
            this.modal.addEventListener('shown.bs.modal', () => {
                form.querySelector('.modal-body').scrollTop = 0;
                (form.querySelector('[aria-invalid="true"]') || this.input('Name')).focus();
            });
            form.addEventListener('submit', event => {
                if (Object.values(this.loaders).some(loader => loader.loading) || this.saving) {
                    event.preventDefault();
                    return;
                }
                this.saving = true;
                this.updateSaveState();
            });
            if (state.retry) {
                this.setMode(state.retry === 'Update');
                document.getElementById('module-audit-comment').value = state.auditComment || '';
                for (const [name, value] of [['ContainerId', state.containerId], ['ServiceName', state.serviceName]]) {
                    if (value) this.input(name).replaceChildren(new Option('Previously selected option', value, true, true));
                }
                this.configuration.apply();
                bootstrap.Modal.getOrCreateInstance(this.modal).show();
            }
        }

        input(name) { return document.getElementById(`Module_${name}`); }
        field(name) { return document.getElementById(`module-${name}-field`); }

        setMode(editing) {
            this.saving = false;
            this.form.action = editing ? this.form.dataset.updateUrl : this.form.dataset.addUrl;
            this.saveLabel = editing ? 'Save changes' : 'Add module';
            document.getElementById('module-modal-title').textContent = editing ? 'Edit module' : 'Add module';
            const comment = document.getElementById('module-audit-comment');
            this.field('audit-comment').classList.toggle('d-none', !editing);
            comment.disabled = !editing;
            comment.required = editing;
            comment.value = '';
            this.updateSaveState();
        }

        open(data) {
            this.form.reset();
            const summary = this.form.querySelector('.validation-summary-errors');
            if (summary) {
                summary.replaceChildren();
                summary.classList.replace('validation-summary-errors', 'validation-summary-valid');
            }
            this.form.querySelector('[data-module-form-error]')?.remove();
            this.form.querySelectorAll('.input-validation-error').forEach(input => input.classList.remove('input-validation-error'));
            this.setMode(Boolean(data));
            const mapping = {
                Id: 'moduleId', Name: 'moduleName', Description: 'moduleDescription', HostId: 'moduleHostId',
                TypeId: 'moduleTypeId', HealthCheckUrl: 'moduleHealthUrl', ManagementUrl: 'moduleManagementUrl',
                ContainerId: 'moduleContainerId', ServiceName: 'moduleServiceName',
                MinecraftServerAddress: 'moduleMinecraftAddress', MinecraftServerPort: 'moduleMinecraftPort',
                SteamServerAddress: 'moduleSteamAddress', SteamQueryPort: 'moduleSteamPort'
            };
            const defaults = { Id: '0', MinecraftServerPort: '25565', SteamQueryPort: '27015' };
            for (const [name, key] of Object.entries(mapping)) {
                if (name === 'TypeId' && !data) continue;
                const value = data?.[key] || defaults[name] || '';
                if (name === 'ContainerId' || name === 'ServiceName')
                    this.input(name).replaceChildren(new Option('Select an option', value, true, true));
                this.input(name).value = value;
            }
            // Checkbox tag helpers also emit a hidden input with the same name.
            document.getElementById('Module_IsEnabled').checked = !data || data.moduleEnabled === 'true';
            document.getElementById('Module_SteamQueryPlayers').checked = data?.moduleSteamPlayers === 'true';
            this.configuration.apply();
            this.form.querySelectorAll('select[data-searchable-select]').forEach(refreshSelect);
        }

        updateSaveState() {
            const loading = Object.values(this.loaders).some(loader => loader.loading);
            this.save.disabled = Boolean(this.saving || loading);
            this.save.textContent = this.saving ? 'Saving…' : loading ? 'Loading options…' : this.saveLabel || 'Add module';
        }
    }

    const form = document.getElementById('module-form');
    if (form) new ModuleEditor(form, JSON.parse(document.getElementById('module-editor-state').textContent));
})();
