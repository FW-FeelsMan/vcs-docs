// wwwroot/js/support/support-sidebar.js

// Загружаем внешний скрипт один раз (для ленивой подгрузки страниц)
function loadScriptOnce(src, id) {
    return new Promise((resolve, reject) => {
        if (id && document.getElementById(id)) return resolve();
        const s = document.createElement('script');
        if (id) s.id = id;
        s.src = src;
        s.defer = true;
        s.onload = resolve;
        s.onerror = () => reject(new Error('failed to load ' + src));
        document.body.appendChild(s);
    });
}

// Подключаем специфичные скрипты под контент и инициализируем их
async function ensureContentScripts(contentId, panelEl) {
    if (contentId === 'accounts') {
        await loadScriptOnce('/js/support/accountsRender.js', 'accounts-render-js');
        if (typeof window.initAccountsPage === 'function') {
            window.initAccountsPage(panelEl); // передаем корневой элемент вставленной панели
        }
    }
    // сюда по мере надобности добавляем обработку других страниц
}

(function () {
    const $ = (sel, root = document) => root.querySelector(sel);
    const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));

    // --- определяем роль (из window.supportRole или по наличию кнопок)
    function detectRole() {
        if (typeof window.supportRole === 'string' && window.supportRole) return window.supportRole;
        const isAdmin = !!$('#btn-workload');                 // у админа есть "Нагрузка"
        const isAgent = !!$('#btn-accounts') && !!$('#btn-tickets');
        if (isAdmin) return 'SupportAdmin';
        if (isAgent) return 'SupportAgent';
        return 'BaseUser';
    }
    const ROLE = detectRole();
    console.log('[support] role:', ROLE);

    // --- маршруты для каждого contentId
    const routes = {
        SupportAdmin: {
            user_tickets: '/Content/Operators/all_open_usertickets',
            closed_tickets: '/Content/Operators/all_close_userticket',
            accounts: '/Content/Operators/accounts',
            workload: '/Content/Operators/workload',
        },
        SupportAgent: {
            user_tickets: '/Content/Operators/all_open_usertickets',
            closed_tickets: '/Content/Operators/all_close_userticket',
            accounts: '/Content/Operators/accounts',
        },
        BaseUser: {
            open_tickets: '/Content/Users/user_open_tickets',
            closed_tickets: '/Content/Users/user_closed_tickets',
            faq: '/Content/Users/faq',
        }
    };

    function mapContentToUrl(contentId) {
        // тривиальная валидация ключа
        if (!/^[a-z0-9_]+$/i.test(contentId)) return null;
        const map = routes[ROLE] || routes.BaseUser;
        if (!Object.prototype.hasOwnProperty.call(map, contentId)) return null;
        return map[contentId];
    }

    // --- лоадер
    const showLoader = () => $('#loader')?.classList.remove('hidden');
    const hideLoader = () => $('#loader')?.classList.add('hidden');

    // защита от дребезга и повторной загрузки одного и того же контента
    let clickLock = false;
    let currentContentId = null;

    // --- выбор пункта и загрузка
    window.selectButton = function (button) {
        if (!button || clickLock) return;

        const contentId = button.getAttribute('data-content');
        if (!contentId) return;
        if (currentContentId === contentId) {
            // просто подсветим выбранный пункт
            $$('.sidebar-button').forEach(b => b.classList.remove('selected'));
            button.classList.add('selected');
            return;
        }

        const url = mapContentToUrl(contentId);
        const container = $('#content');
        if (!container || !url) {
            console.warn('[support] blocked/unknown content id:', contentId);
            return;
        }

        // визуал выбранной кнопки
        $$('.sidebar-button').forEach(b => b.classList.remove('selected'));
        button.classList.add('selected');

        clickLock = true;
        setTimeout(() => (clickLock = false), 300);

        showLoader();

        fetch(url, { credentials: 'same-origin', cache: 'no-store' })
            .then(r => { if (!r.ok) throw new Error(`HTTP ${r.status}`); return r.text(); })
            .then(html => {
                const panel = document.createElement('div');
                panel.className = 'view-panel view-pre';
                panel.innerHTML = html;
                container.replaceChildren(panel);

                // лениво подгружаем и инициализируем спец-скрипты для контента
                ensureContentScripts(contentId, panel).catch(console.error);

                // аккуратная анимация появления
                panel.getBoundingClientRect(); // reflow
                panel.classList.add('view-enter');
                panel.addEventListener('animationend', () => {
                    panel.classList.remove('view-enter', 'view-pre');
                    hideLoader();
                }, { once: true });

                currentContentId = contentId;
            })
            .catch(err => {
                console.error('[support] load error:', err);
                container.innerHTML = `<div style="padding:16px;color:#ddd">Ошибка загрузки: ${contentId}</div>`;
                hideLoader();
            });
    };

    // --- инициализация
    document.addEventListener('DOMContentLoaded', () => {
        $$('.sidebar-button').forEach(btn => btn.addEventListener('click', () => window.selectButton(btn)));
        // выбрать первый элемент сайдбара
        const first = $('.sidebar .sidebar-button');
        if (first) window.selectButton(first);
    });
})();
