// wwwroot/js/support/support-sidebar.js  (simple + auth-guard + verbose logs)

// ---- utils ----
function loadScriptOnce(src, id) {
    return new Promise((resolve, reject) => {
        if (id && document.getElementById(id)) return resolve();
        const s = document.createElement("script");
        if (id) s.id = id;
        s.src = src;
        s.defer = true;
        s.onload = () => resolve();
        s.onerror = () => reject(new Error("failed to load " + src));
        document.body.appendChild(s);
    });
}

function waitForElm(selector, timeout = 5000, root = document) {
    return new Promise((resolve, reject) => {
        const first = root.querySelector(selector);
        if (first) return resolve(first);
        const obs = new MutationObserver(() => {
            const el = root.querySelector(selector);
            if (el) { obs.disconnect(); resolve(el); }
        });
        obs.observe(root, { childList: true, subtree: true });
        const to = setTimeout(() => { obs.disconnect(); reject(new Error("container timeout: " + selector)); }, timeout);
        const guard = new MutationObserver(() => {
            if (!document.body.contains(root)) {
                guard.disconnect(); clearTimeout(to); obs.disconnect();
                reject(new Error("root removed"));
            }
        });
        guard.observe(document.documentElement, { childList: true, subtree: true });
    });
}

// ---- per-panel loader ----
// wwwroot/js/support/support-sidebar.js
async function ensureContentScripts(contentId, panelEl) {
    if (contentId === "accounts") {
        await loadScriptOnce("/js/support/accountsRender.js", "accounts-render-js");
        try { await waitForElm("#accountsTable", 3000, panelEl); } catch { }
        if (!panelEl.isConnected) return;
        if (typeof window.initAccountsPage === "function") await window.initAccountsPage(panelEl);
        return;
    }

    if (contentId === "workload") {
        await loadScriptOnce("https://cdn.jsdelivr.net/npm/chart.js@4.4.1/dist/chart.umd.min.js", "chartjs-4");
        await loadScriptOnce("/js/operator/workload.js", "workload-js");
        try { await waitForElm("#op-workload", 3000, panelEl); } catch { }
        if (!panelEl.isConnected) return;
        if (typeof window.initWorkload === "function") await window.initWorkload(panelEl);
        return;
    }

    if (contentId === "user_tickets") {
        await loadScriptOnce("/js/operator/all_open_userticket.js", "open-ticket-js");
        try { await waitForElm("#op-open-tickets", 3000, panelEl); } catch { }
        if (!panelEl.isConnected) return;
        if (typeof window.initAllOpenUserTickets === "function") {
            await window.initAllOpenUserTickets(panelEl);
        } else {
            console.warn("[sidebar] initAllOpenUserTickets not found");
        }
        return;
    }

    if (contentId === "closed_tickets") {
        await loadScriptOnce("/js/operator/all_close_usertickets.js", "closed-tickets-js");
        try { await waitForElm("#op-close-tickets", 3000, panelEl); } catch { }
        if (!panelEl.isConnected) return;
        if (typeof window.initAllCloseUserTickets === "function") {
            await window.initAllCloseUserTickets(panelEl);
        } else {
            console.warn("[sidebar] initAllCloseUserTickets not found");
        }
        return;
    }

    if (contentId === "open_tickets") {
        await loadScriptOnce("/js/user/user_open_tickets.js", "user-open-tickets-js");
        try { await waitForElm("#user-open-tickets", 3000, panelEl); } catch { }
        if (!panelEl.isConnected) return;
        if (typeof window.initUserOpenTickets === "function") await window.initUserOpenTickets(panelEl);
        return;
    }

    if (contentId === "faq") {
        await loadScriptOnce("/js/user/faq.js", "user-faq-js");
        try { await waitForElm("#user-faq", 3000, panelEl); } catch { }
        if (!panelEl.isConnected) return;
        if (typeof window.initUserFaq === "function") await window.initUserFaq(panelEl);
        return;
    }
}

// ---- main ----
(function () {
    const $ = (s, r = document) => r.querySelector(s);
    const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));

    function detectRole() {
        if (typeof window.supportRole === "string" && window.supportRole) return window.supportRole;
        const isAdmin = !!$("#btn-workload");
        const isAgent = !!$("#btn-accounts") && !!$("#btn-tickets");
        if (isAdmin) return "SupportAdmin";
        if (isAgent) return "SupportAgent";
        return "BaseUser";
    }
    const ROLE = detectRole();
    console.log("[sidebar] role:", ROLE);

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

    const showLoader = () => $("#loader")?.classList.remove("hidden");
    const hideLoader = () => $("#loader")?.classList.add("hidden");

    let clickLock = false;
    let currentContentId = null;
    let currentDispose = null;

    // --- small auth guard ---
    function looksLikeLogin(html) {
        if (!html || typeof html !== "string") return false;
        const t = html.toLowerCase();
        return t.includes("/account/loginsupport")
            || t.includes("name=\"password\"")
            || t.includes("войти</button>")     // русская кнопка
            || t.includes("<h3>вход</h3>")      // твой шаблон
            || t.includes(">вход<");
    }

    async function fetchHtml(url) {
        console.log("[sidebar] fetch:", url);
        const res = await fetch(url, {
            credentials: "same-origin",
            cache: "no-store",
            headers: { "X-Requested-With": "fetch" },
            redirect: "follow"
        });
        const ct = (res.headers.get("content-type") || "").toLowerCase();
        const txt = await res.text();
        console.log("[sidebar] fetch resp:", { status: res.status, redirected: res.redirected, url: res.url, ct });

        // если сервер кинул редирект/401/403 или прислал логин-форму — уходим полноэкранно
        if (res.redirected || res.status === 401 || res.status === 403 || looksLikeLogin(txt)) {
            //const to = res.url && /\/account\/loginsupport/i.test(res.url) ? res.url : "/Account/LoginSupport";
            let to = res.url && /\/account\/loginsupport/i.test(res.url) ? res.url : "/Account/LoginSupport";
            if (!/[?&]forced=1\b/i.test(to)) {
                to += (to.includes("?") ? "&" : "?") + "forced=1";
            }
            console.warn("[sidebar] auth-guard: redirecting to", to);
            window.location.href = to;
            throw new Error("auth-redirect");
        }

        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        if (!ct.includes("text/html")) throw new Error("Unexpected content-type: " + ct);
        return txt;
    }

    // ---- selection handler ----
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
            console.warn("[sidebar] unknown content id:", contentId);
            return;
        }

        $$(".sidebar-button").forEach((b) => b.classList.remove("selected"));
        button.classList.add("selected");

        clickLock = true;
        setTimeout(() => (clickLock = false), 300);

        showLoader();
        try {
            const html = await fetchHtml(url);

            const panel = document.createElement("div");
            panel.className = "view-panel view-pre";
            panel.innerHTML = html;
            try { currentDispose?.(); } catch { }
            container.replaceChildren(panel);

            // дать браузеру дорисовать
            panel.getBoundingClientRect();

            await ensureContentScripts(contentId, panel);
            if (typeof panel.__dispose === "function") {
                currentDispose = panel.__dispose;
            } else {
                currentDispose = null;
            }
            panel.classList.add("view-enter");
            panel.addEventListener("animationend", () => {
                panel.classList.remove("view-enter", "view-pre");
                hideLoader();
            }, { once: true });

            currentContentId = contentId;
            document.dispatchEvent(new CustomEvent("SupportContentChanged", { detail: { contentId } }));
            console.log("[sidebar] content loaded:", contentId);
        } catch (err) {
            if (String(err && err.message || "").includes("auth-redirect")) return;
            console.error("[sidebar] load error:", err);
            const container = $("#content");
            if (container) container.innerHTML = `<div style="padding:16px;color:#ddd">Ошибка загрузки: ${contentId}</div>`;
            hideLoader();
        }
    };

    // ---- init ----
    document.addEventListener("DOMContentLoaded", () => {
        console.log("[sidebar] DOMContentLoaded");
        $$(".sidebar-button").forEach((btn) =>
            btn.addEventListener("click", () => window.selectButton(btn))
        );
        const first = $(".sidebar .sidebar-button");
        if (first) window.selectButton(first);
    });
})();
