// wwwroot/js/support/support-sidebar.js

// ---- утилиты ----
function loadScriptOnce(src, id) {
    return new Promise((resolve, reject) => {
        if (id && document.getElementById(id)) return resolve();
        const s = document.createElement("script");
        if (id) s.id = id;
        s.src = src;
        s.defer = true;
        s.onload = resolve;
        s.onerror = () => reject(new Error("failed to load " + src));
        document.body.appendChild(s);
    });
}

// Ждём появления элемента (в пределах root, если задан)
function waitForElm(selector, timeout = 5000, root = document) {
    return new Promise((resolve, reject) => {
        const initial = root.querySelector(selector);
        if (initial) return resolve(initial);

        const obs = new MutationObserver(() => {
            const found = root.querySelector(selector);
            if (found) {
                obs.disconnect();
                resolve(found);
            }
        });
        obs.observe(root, { childList: true, subtree: true });

        const to = setTimeout(() => {
            obs.disconnect();
            reject(new Error("container timeout: " + selector));
        }, timeout);

        // если вдруг root будет удалён — тоже завершаем ожидание
        const guard = new MutationObserver(() => {
            if (!document.body.contains(root)) {
                guard.disconnect();
                clearTimeout(to);
                obs.disconnect();
                reject(new Error("root removed"));
            }
        });
        guard.observe(document.documentElement, { childList: true, subtree: true });
    });
}

// ---- ленивые скрипты под конкретные панели ----
async function ensureContentScripts(contentId, panelEl) {
    if (contentId === "accounts") {
        // подгружаем отрисовщик
        await loadScriptOnce("/js/support/accountsRender.js", "accounts-render-js");
        // ждём контейнер таблицы, чтобы не ловить «контейнеры не найдены»
        try {
            await waitForElm("#accountsTable", 3000, panelEl);
        } catch { /* не критично, init сам перепроверит */ }
        if (typeof window.initAccountsPage === "function") {
            // init возвращает Promise — дождёмся, чтобы не рвать гонку
            await window.initAccountsPage(panelEl);
        }
    }
    // сюда добавляй обработку других контент-страниц по мере надобности
}

(function () {
    const $ = (sel, root = document) => root.querySelector(sel);
    const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));

    // ---- определяем роль ----
    function detectRole() {
        if (typeof window.supportRole === "string" && window.supportRole) return window.supportRole;
        const isAdmin = !!$("#btn-workload"); // у админа есть "Нагрузка"
        const isAgent = !!$("#btn-accounts") && !!$("#btn-tickets");
        if (isAdmin) return "SupportAdmin";
        if (isAgent) return "SupportAgent";
        return "BaseUser";
    }
    const ROLE = detectRole();
    console.log("[support] role:", ROLE);

    // ---- маршруты ----
    const routes = {
        SupportAdmin: {
            user_tickets: "/Content/Operators/all_open_usertickets",
            closed_tickets: "/Content/Operators/all_close_userticket",
            accounts: "/Content/Operators/accounts",
            workload: "/Content/Operators/workload",
        },
        SupportAgent: {
            user_tickets: "/Content/Operators/all_open_usertickets",
            closed_tickets: "/Content/Operators/all_close_userticket",
            accounts: "/Content/Operators/accounts",
        },
        BaseUser: {
            open_tickets: "/Content/Users/user_open_tickets",
            closed_tickets: "/Content/Users/user_closed_tickets",
            faq: "/Content/Users/faq",
        },
    };

    function mapContentToUrl(contentId) {
        if (!/^[a-z0-9_]+$/i.test(contentId)) return null;
        const map = routes[ROLE] || routes.BaseUser;
        if (!Object.prototype.hasOwnProperty.call(map, contentId)) return null;
        return map[contentId];
    }

    // ---- лоадер ----
    const showLoader = () => $("#loader")?.classList.remove("hidden");
    const hideLoader = () => $("#loader")?.classList.add("hidden");

    // защита от дребезга и повторной загрузки одного и того же контента
    let clickLock = false;
    let currentContentId = null;

    // ---- выбор пункта и загрузка ----
    window.selectButton = async function (button) {
        if (!button || clickLock) return;

        const contentId = button.getAttribute("data-content");
        if (!contentId) return;
        if (currentContentId === contentId) {
            $$(".sidebar-button").forEach((b) => b.classList.remove("selected"));
            button.classList.add("selected");
            return;
        }

        const url = mapContentToUrl(contentId);
        const container = $("#content");
        if (!container || !url) {
            console.warn("[support] blocked/unknown content id:", contentId);
            return;
        }

        $$(".sidebar-button").forEach((b) => b.classList.remove("selected"));
        button.classList.add("selected");

        clickLock = true;
        setTimeout(() => (clickLock = false), 300);

        showLoader();

        try {
            const r = await fetch(url, { credentials: "same-origin", cache: "no-store" });
            if (!r.ok) throw new Error(`HTTP ${r.status}`);
            const html = await r.text();

            const panel = document.createElement("div");
            panel.className = "view-panel view-pre";
            panel.innerHTML = html;
            container.replaceChildren(panel);

            // даём браузеру дорисовать DOM и только потом инициализировать спец-скрипты
            panel.getBoundingClientRect(); // reflow
            await ensureContentScripts(contentId, panel);

            // анимация появления
            panel.classList.add("view-enter");
            panel.addEventListener(
                "animationend",
                () => {
                    panel.classList.remove("view-enter", "view-pre");
                    hideLoader();
                },
                { once: true }
            );

            currentContentId = contentId;
            document.dispatchEvent(new CustomEvent("SupportContentChanged", { detail: { contentId } }));
        } catch (err) {
            console.error("[support] load error:", err);
            container.innerHTML = `<div style="padding:16px;color:#ddd">Ошибка загрузки: ${contentId}</div>`;
            hideLoader();
        }
    };

    // ---- инициализация ----
    document.addEventListener("DOMContentLoaded", () => {
        $$(".sidebar-button").forEach((btn) =>
            btn.addEventListener("click", () => window.selectButton(btn))
        );
        const first = $(".sidebar .sidebar-button");
        if (first) window.selectButton(first);
    });
})();
