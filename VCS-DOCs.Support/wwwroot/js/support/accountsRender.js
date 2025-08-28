// wwwroot/js/support/accountsRender.js
// Actions UI = как в веб-хранилище: primary-кнопка + стрелка + выпадайка.
// Действия: Принуд. выход (по умолчанию), Бан/Активировать, Переключение ролей (только для админа).

(function () {
    function init(root) {
        const $ = (s, r = root || document) => r.querySelector(s);
        const $$ = (s, r = root || document) => Array.from(r.querySelectorAll(s));

        // Признак админа: на странице есть кнопка "Нагрузка"
        const isAdmin = !!document.getElementById('btn-workload');

        // CSRF токен: meta или скрытый input
        function getCsrfToken() {
            const meta = document.querySelector('meta[name="csrf-token"]')?.content;
            if (meta) return meta;
            const input = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (input) return input;
            return '';
        }
        const token = getCsrfToken();

        const tbody = $('#accountsBody');
        const searchEl = $('#searchBox');
        const roleSel = $('#roleFilter');

        if (!tbody || !searchEl || !roleSel) {
            console.warn('[accounts] init: контейнеры не найдены (возможно, страница ещё не вставлена)');
            return;
        }

        // ===== API helpers =====
        async function post(url, body) {
            const res = await fetch(url, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token
                },
                body: JSON.stringify(body || {})
            });
            if (!res.ok) {
                const t = await res.text().catch(() => '');
                throw new Error(`HTTP ${res.status} ${t}`);
            }
            // некоторые POST могут вернуть пусто — не падаем на JSON.parse
            try { return await res.json(); } catch { return {}; }
        }

        // ===== Data load/render =====
        async function loadAccounts() {
            const role = roleSel.value;
            const query = searchEl.value?.trim() ?? '';

            const url = new URL('/api/support/accounts', location.origin);
            if (role) url.searchParams.set('role', role);
            if (query) url.searchParams.set('q', query);

            tbody.innerHTML = `<tr><td colspan="7">Загрузка…</td></tr>`;
            try {
                const res = await fetch(url, { credentials: 'same-origin', cache: 'no-store' });
                if (!res.ok) throw new Error('HTTP ' + res.status);
                const data = await res.json();
                renderRows(data);
            } catch (e) {
                console.error('[accounts] load error', e);
                tbody.innerHTML = `<tr><td colspan="7" style="color:#c33;">Ошибка загрузки</td></tr>`;
            }
        }

        function pill(text, cls = '') {
            return `<span class="badge ${cls}" style="display:inline-block;padding:2px 8px;border-radius:999px;font-size:.85rem;font-weight:700;border:1px solid transparent;background:#eef3f9;color:#2563eb;margin:0 4px 4px 0;">${text}</span>`;
        }

        function renderActionsMultibutton(u) {
            const accessOn = (u.access ?? 0) > 0;
            const banOrActLabel = accessOn ? 'Бан' : 'Активировать';

            // admin-only доп. пункты (переключение ролей + бан/активировать)
            const adminItems = isAdmin ? `
                <div class="dropdown-item sep"></div>
                <div class="dropdown-item" data-act="role" data-role="BaseUser"       data-id="${u.id}">Переключить роль: BaseUser</div>
                <div class="dropdown-item" data-act="role" data-role="SupportAgent"   data-id="${u.id}">Переключить роль: SupportAgent</div>
                <div class="dropdown-item" data-act="role" data-role="SupportAdmin"   data-id="${u.id}">Переключить роль: SupportAdmin</div>
                <div class="dropdown-item sep"></div>
                <div class="dropdown-item" data-act="${accessOn ? 'ban' : 'activate'}" data-id="${u.id}">${banOrActLabel}</div>
            ` : '';

            return `
            <div class="multi-button compact action-multibutton" style="position:relative;"
                 data-id="${u.id}" data-access="${u.access ?? 0}">
                <button class="button-sliding primary compact action-button"
                        data-act="forceLogout" data-id="${u.id}">
                    Принуд. выход
                </button>
                <div class="dropdown-arrow compact" tabindex="0">▾</div>
                <div class="action-dropdown-menu compact" style="display:none; position:absolute; min-width:180px;">
                    <div class="dropdown-item" data-act="forceLogout" data-id="${u.id}">Принуд. выход</div>
                    ${adminItems}
                </div>
            </div>`;
        }

        function renderRows(list) {
            if (!Array.isArray(list) || list.length === 0) {
                tbody.innerHTML = `<tr><td colspan="7">Ничего не найдено</td></tr>`;
                return;
            }

            const rows = list.map(u => {
                const roles = (u.roles || []).map(r => pill(r)).join(' ');
                const online = u.isOnline
                    ? '<span class="user-status online">В сети</span>'
                    : '<span class="user-status offline">Оффлайн</span>';
                const access = (u.access ?? 0) > 0
                    ? '<span class="badge badge--ok">Активен</span>'
                    : '<span class="badge badge--blocked">Заблокирован</span>';

                // lastSeen уже строка "yyyy-MM-dd HH:mm:ss" с бэка
                const last = u.lastSeen || u.lastEntry || '';

                return `
                <tr data-id="${u.id}">
                    <td>${u.userName ?? ''}</td>
                    <td>${u.fullName ?? ''}</td>
                    <td>${roles || '-'}</td>
                    <td>${online}</td>
                    <td>${access}</td>
                    <td>${last}</td>
                    <td>${renderActionsMultibutton(u)}</td>
                </tr>`;
            });

            tbody.innerHTML = rows.join('');

            setupActionDropdowns();
        }

        // ===== UI wiring for multibuttons (как в веб-хранилище) =====
        function setupActionDropdowns() {
            // Сначала закрываем все меню
            function closeAllMenus(except) {
                $$('#accountsTable .action-dropdown-menu').forEach(m => {
                    if (m !== except) m.style.display = 'none';
                });
            }

            $$('#accountsTable .action-multibutton').forEach(box => {
                const btn = box.querySelector('.action-button');
                const arrow = box.querySelector('.dropdown-arrow');
                const menu = box.querySelector('.action-dropdown-menu');
                if (!btn || !arrow || !menu) return;

                let selectedAct = btn.dataset.act || 'forceLogout';
                let selectedRole = btn.dataset.role || ''; // используется, если выбрана команда "role"

                // Открытие/закрытие выпадайки
                arrow.onclick = (e) => {
                    e.stopPropagation();
                    const isOpen = menu.style.display === 'block';
                    closeAllMenus(isOpen ? null : menu);
                    menu.style.display = isOpen ? 'none' : 'block';
                };

                // Клик по primary — выполнить выбранное действие
                btn.onclick = async () => {
                    await runAction(selectedAct, box.dataset.id, selectedRole, box);
                };

                // Выбор в меню: меняем подпись primary и сразу выполняем (поведение как в веб-хранилище)
                menu.querySelectorAll('.dropdown-item').forEach(item => {
                    item.onclick = async () => {
                        const act = item.dataset.act;
                        const id = item.dataset.id;
                        selectedAct = act;

                        // подпись на primary = текст пункта меню
                        btn.textContent = item.textContent.trim();
                        btn.dataset.act = act;

                        // если выбрана роль — запоминаем
                        if (act === 'role') {
                            selectedRole = item.dataset.role || '';
                            btn.dataset.role = selectedRole;
                        } else {
                            selectedRole = '';
                            btn.removeAttribute('data-role');
                        }

                        menu.style.display = 'none';
                        await runAction(act, id, selectedRole, box);
                    };
                });
            });

            // Клик вне — закрыть все
            document.addEventListener('click', onDocClickClose, { once: true });
            function onDocClickClose(e) {
                const anyOpen = $$('#accountsTable .action-dropdown-menu').some(m => m.style.display === 'block');
                if (!anyOpen) return;
                // если клик не по меню и не по стрелке
                if (!e.target.closest('.action-multibutton')) {
                    closeAllMenus(null);
                } else {
                    // повторно навешиваем, пока что-то открывается
                    document.addEventListener('click', onDocClickClose, { once: true });
                }
            }
        }

        // Выполнение действий
        async function runAction(act, id, role, box) {
            try {
                if (act === 'forceLogout') {
                    await post(`/api/support/accounts/${id}/force-logout`, {});
                } else if (act === 'role') {
                    if (!isAdmin) return;
                    if (!role) return;
                    await post(`/api/support/accounts/${id}/toggle-role`, { role });
                } else if (act === 'ban' || act === 'activate') {
                    if (!isAdmin) return;
                    // У нас один endpoint toggle-access — вызываем его, чтобы переключить состояние
                    await post(`/api/support/accounts/${id}/toggle-access`, {});
                }
                await loadAccounts();
            } catch (e) {
                console.error('[accounts] action error', e);
                alert('Ошибка: ' + e.message);
            }
        }

        // ===== Filters & search =====
        roleSel.addEventListener('change', loadAccounts);
        let searchTimer = null;
        searchEl.addEventListener('input', () => {
            clearTimeout(searchTimer);
            searchTimer = setTimeout(loadAccounts, 250);
        });

        // ===== Start =====
        loadAccounts();
    }

    // экспорт инициализатора (чтобы вызвать из сайдбара)
    window.initAccountsPage = init;
})();
