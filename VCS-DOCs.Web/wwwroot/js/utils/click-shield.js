// wwwroot/js/utils/click-shield.js
(() => {
    const BUSY_MS_DEFAULT = 1200;

    // Делегат на весь документ — ловим клики на ранней фазе
    document.addEventListener('click', (e) => {
        const target = e.target.closest('[data-click]');
        if (!target) return;

        // варианты: data-click="once", "debounce:300", "throttle:800"
        const mode = target.dataset.click || 'once';

        // уже «занят»?
        if (target.dataset.busy === '1') { e.preventDefault(); e.stopPropagation(); return; }

        const now = Date.now();
        const last = +(target.dataset.lastClickTs || 0);

        if (mode.startsWith('debounce')) {
            const ms = +mode.split(':')[1] || 300;
            clearTimeout(target.__debounceTimer);
            target.__debounceTimer = setTimeout(() => doFire(target), ms);
            e.preventDefault(); e.stopPropagation();
            return;
        }

        if (mode.startsWith('throttle')) {
            const ms = +mode.split(':')[1] || 800;
            if (now - last < ms) { e.preventDefault(); e.stopPropagation(); return; }
            target.dataset.lastClickTs = String(now);
        }

        // once (по умолчанию) — блокируем до BUSY_MS_DEFAULT
        target.dataset.busy = '1';
        target.setAttribute('aria-busy', 'true');
        target.classList.add('is-busy');
        setTimeout(() => {
            target.dataset.busy = '0';
            target.removeAttribute('aria-busy');
            target.classList.remove('is-busy');
        }, +(target.dataset.busyMs || BUSY_MS_DEFAULT));
    }, true);

    function doFire(btn) {
        // руками триггерим нативный click (раздебоученный)
        btn.click?.();
    }

    // Обработка submit — дизейбл всех submit-кнопок формы на время отправки
    document.addEventListener('submit', (e) => {
        const form = e.target;
        if (!(form instanceof HTMLFormElement)) return;
        const buttons = form.querySelectorAll('button[type=submit], input[type=submit]');
        buttons.forEach(b => { b.disabled = true; b.classList.add('is-busy'); b.dataset.busy = '1'; });
        // на клиентских ошибок/валидации включим обратно
        form.addEventListener('invalid', () => {
            buttons.forEach(b => { b.disabled = false; b.classList.remove('is-busy'); b.dataset.busy = '0'; });
        }, { capture: true, once: true });
    }, true);

    // Обёртка над fetch с идемпотентным ключом
    window.postJsonSafe = async function (url, body, opts = {}) {
        const key = (crypto?.randomUUID?.() || (Date.now() + ':' + Math.random())).toString();
        const headers = Object.assign({
            'Content-Type': 'application/json',
            'X-Idempotency-Key': key
        }, opts.headers || {});
        return fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            cache: 'no-store',
            headers,
            body: JSON.stringify(body || {}),
            signal: opts.signal
        });
    };
})();
