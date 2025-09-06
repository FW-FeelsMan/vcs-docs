// wwwroot/js/utils/support-click-shield.js
(() => {
    const BUSY_MS_DEFAULT = 1200;
    const BUSY_CLASS = "is-busy";

    // -------- Click shield (делегирование на документ) --------
    document.addEventListener(
        "click",
        (e) => {
            const target = e.target?.closest?.("[data-click]");
            if (!target) return;

            // пропускаем клики, инициированные debounce-блоком
            if (target.dataset.skipShield === "1") return;

            const mode = (target.dataset.click || "once").trim().toLowerCase();

            // уже «занят» — режем
            if (target.dataset.busy === "1") {
                e.preventDefault();
                e.stopPropagation();
                return;
            }

            const now = Date.now();
            const last = +(target.dataset.lastClickTs || 0);

            // --- Debounce ---
            if (mode.startsWith("debounce")) {
                const ms = +mode.split(":")[1] || 300;
                clearTimeout(target.__debounceTimer);
                target.__debounceTimer = setTimeout(() => {
                    // один «нативный» клик без повторной фильтрации
                    target.dataset.skipShield = "1";
                    try {
                        target.click?.();
                    } finally {
                        setTimeout(() => {
                            delete target.dataset.skipShield;
                        }, 0);
                    }
                }, ms);
                e.preventDefault();
                e.stopPropagation();
                return;
            }

            // --- Throttle ---
            if (mode.startsWith("throttle")) {
                const ms = +mode.split(":")[1] || 800;
                if (now - last < ms) {
                    e.preventDefault();
                    e.stopPropagation();
                    return;
                }
                target.dataset.lastClickTs = String(now);

                // визуально подблокируем ровно на интервал троттла
                lockVisual(target, ms);
                return; // для throttle дополнительных действий не нужно
            }

            // --- Once (по умолчанию) ---
            const busyMs = +(target.dataset.busyMs || BUSY_MS_DEFAULT);
            lockVisual(target, busyMs);
            // ничего не предотвращаем: событие пойдёт дальше один раз
        },
        true
    );

    function lockVisual(el, ms) {
        el.dataset.busy = "1";
        el.setAttribute("aria-busy", "true");
        el.classList.add(BUSY_CLASS);
        if ("disabled" in el) el.disabled = true;

        setTimeout(() => {
            el.dataset.busy = "0";
            el.removeAttribute("aria-busy");
            el.classList.remove(BUSY_CLASS);
            if ("disabled" in el) el.disabled = false;
        }, ms);
    }

    // -------- Submit shield (блок submit-кнопок формы на время отправки) --------
    document.addEventListener(
        "submit",
        (e) => {
            const form = e.target;
            if (!(form instanceof HTMLFormElement)) return;

            const buttons = form.querySelectorAll(
                'button[type="submit"], input[type="submit"]'
            );
            buttons.forEach((b) => {
                b.disabled = true;
                b.classList.add(BUSY_CLASS);
                b.dataset.busy = "1";
            });

            // При клиентской невалидности вернём кнопки
            form.addEventListener(
                "invalid",
                () => {
                    buttons.forEach((b) => {
                        b.disabled = false;
                        b.classList.remove(BUSY_CLASS);
                        b.dataset.busy = "0";
                    });
                },
                { capture: true, once: true }
            );
        },
        true
    );

    // ============================== ИДЕМПОТЕНТНОСТЬ ==============================

    // 1) Автопатч fetch: добавляет X-Idempotency-Key на POST/PUT/PATCH/DELETE,
    //    не трогая FormData и уже заданные ключи/заголовки.
    (() => {
        const origFetch = window.fetch;
        window.fetch = function (input, init) {
            const i = init || {};
            const method = (i.method || "GET").toUpperCase();
            // не ломаем уже переданные заголовки (оборачиваем в Headers)
            const headers = new Headers(i.headers || {});

            if (method !== "GET" && method !== "HEAD") {
                if (!headers.has("X-Idempotency-Key")) {
                    const key =
                        (crypto?.randomUUID?.() ||
                            `${Date.now()}-${Math.random().toString(36).slice(2)}`) + "";
                    headers.set("X-Idempotency-Key", key);
                }
                // Content-Type не выставляем насильно — FormData должен идти без него
            }

            i.headers = headers;
            return origFetch(input, i);
        };
    })();

    // 2) Удобные обёртки: добавляют JSON и креды/кэш-политику «по умолчанию»
    function makeHeaders(base, body) {
        const headers = new Headers(base || {});
        if (!(body instanceof FormData) && !headers.has("Content-Type")) {
            headers.set("Content-Type", "application/json");
        }
        return headers;
    }

    window.fetchSafe = function (url, opts = {}) {
        const i = { credentials: "same-origin", cache: "no-store", ...opts };
        // если есть body — аккуратно расставим Content-Type (кроме FormData)
        if (i.body && !(i.body instanceof FormData)) {
            i.headers = makeHeaders(i.headers, i.body);
        }
        return fetch(url, i); // пойдёт через пропатченный fetch (с X-Idempotency-Key)
    };

    window.postJsonSafe = (url, body, opts = {}) =>
        fetchSafe(url, {
            method: "POST",
            body: JSON.stringify(body ?? {}),
            ...opts,
        });

    window.putJsonSafe = (url, body, opts = {}) =>
        fetchSafe(url, {
            method: "PUT",
            body: JSON.stringify(body ?? {}),
            ...opts,
        });

    window.patchJsonSafe = (url, body, opts = {}) =>
        fetchSafe(url, {
            method: "PATCH",
            body: JSON.stringify(body ?? {}),
            ...opts,
        });

    window.deleteSafe = (url, opts = {}) =>
        fetchSafe(url, {
            method: "DELETE",
            ...opts,
        });
})();
