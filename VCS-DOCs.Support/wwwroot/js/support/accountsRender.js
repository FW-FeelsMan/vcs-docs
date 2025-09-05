// wwwroot/js/support/accountsRender.js
(function () {
    // пометки для выгрузки (чтобы гасить таймеры и не редиректить)
    let __accounts_disposed = false;
    window.addEventListener("beforeunload", () => { __accounts_disposed = true; stopPolling(); });
    window.addEventListener("pagehide", () => { __accounts_disposed = true; stopPolling(); });

    // ---- helpers ----
    function waitForElm(selector, timeout = 2000, root = document) {
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
            setTimeout(() => {
                obs.disconnect();
                reject(new Error("timeout: " + selector));
            }, timeout);
        });
    }
    async function waitForAll(selectors, root, timeout = 2000) {
        await Promise.all(selectors.map((s) => waitForElm(s, timeout, root)));
    }

    const initOnce = new WeakSet();

    async function init(root) {
        const panel = root || document;
        if (initOnce.has(panel)) return;
        initOnce.add(panel);

        const $ = (s, r = panel) => r.querySelector(s);
        const $$ = (s, r = panel) => Array.from(r.querySelectorAll(s));
        const isAdmin = !!document.getElementById("btn-workload");

        try {
            await waitForAll(["#accountsHead", "#accountsBody", "#searchBox", "#roleFilter", "#orgFilter"], panel, 2500);
        } catch { /* best-effort */ }

        function getCsrfToken() {
            const meta = document.querySelector('meta[name="csrf-token"]')?.content;
            if (meta) return meta;
            const input = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (input) return input;
            return "";
        }

        const token = getCsrfToken();
        const thead = $("#accountsHead");
        const tbody = $("#accountsBody");
        const searchEl = $("#searchBox");
        const roleSel = $("#roleFilter");
        const orgSel = $("#orgFilter");

        if (!thead || !tbody || !searchEl || !roleSel || !orgSel) return;

        async function getJson(url) {
            const res = await fetch(url, { credentials: "same-origin", cache: "no-store" });
            const ct = res.headers.get("content-type") || "";

            if (!res.ok) {
                // Не редиректим на логин, если уже уходим/перезагружаемся
                if (ct.includes("text/html")) {
                    if (!__accounts_disposed) {
                        console.warn("[accounts] non-json (HTML) on non-OK; suppressed redirect");
                    }
                    throw new Error("Non-JSON response");
                }
                const txt = await res.text().catch(() => "");
                throw new Error("HTTP " + res.status + (txt ? " " + txt : ""));
            }

            if (!ct.includes("application/json")) {
                if (!__accounts_disposed && ct.includes("text/html")) {
                    console.warn("[accounts] OK but HTML; suppressed redirect");
                }
                throw new Error("Unexpected content-type: " + ct);
            }
            return res.json();
        }

        async function post(url, body) {
            const res = await fetch(url, {
                method: "POST",
                credentials: "same-origin",
                headers: {
                    "Content-Type": "application/json",
                    "RequestVerificationToken": token
                },
                body: JSON.stringify(body || {})
            });
            if (!res.ok) {
                const t = await res.text().catch(() => "");
                throw new Error(`HTTP ${res.status} ${t}`);
            }
            try { return await res.json(); } catch { return {}; }
        }

        function normalizeAccountsResponse(raw) {
            const defaults = [
                { code: "VSupport", name: "V-Support" },
                { code: "VDocs", name: "V-DOCs" }
            ];
            if (Array.isArray(raw)) {
                const users = raw.map((u) => ({
                    ...u,
                    presence: {
                        VSupport: { online: !!(u.isOnlineVSupport ?? u.isOnline), lastSeen: u.lastSeenVSupport ?? u.lastSeen ?? null },
                        VDocs: { online: !!u.isOnlineVDocs, lastSeen: u.lastSeenVDocs ?? null }
                    },
                    createdAtMs: Date.now()
                }));
                return { projects: defaults, users, organizations: [] };
            }
            const projects = Array.isArray(raw?.projects) && raw.projects.length ? raw.projects : defaults;
            const users = Array.isArray(raw?.users) ? raw.users : [];
            const organizations = Array.isArray(raw?.organizations) ? raw.organizations : [];
            const usersSafe = users.map((u) => ({ ...u, presence: u.presence ?? {}, createdAtMs: Number(u.createdAtMs || 0) }));
            return { projects, users: usersSafe, organizations };
        }

        function buildHead() {
            thead.innerHTML = `
            <tr>
              <th>Логин</th>
              <th>ФИО</th>
              <th>Организация</th>
              <th>Роли</th>
              <th>VCS-DOCs</th>
              <th>VCS-SD</th>
              <th>Доступ</th>
              <th>Входил</th>
              <th>Действия</th>
            </tr>`;
        }

        function populateOrgFilter(list) {
            const current = orgSel.value || "";
            let html = `<option value="">Все организации</option>`;
            const items = (Array.isArray(list) ? list : []).slice().filter(x => x !== null && x !== undefined).map(String);
            const uniq = Array.from(new Set(items)).sort((a, b) => a.localeCompare(b, "ru"));
            for (const o of uniq) html += `<option value="${o}">${o}</option>`;
            orgSel.innerHTML = html;
            if (current && uniq.includes(current)) orgSel.value = current;
        }

        const badge = (text) => `<span class="badge" style="display:inline-block;padding:2px 8px;border-radius:999px;font-size:.85rem;font-weight:700;border:1px solid transparent;background:#eef3f9;color:#2563eb;margin:0 4px 4px 0;">${text}</span>`;
        const statusPill = (on) => on ? '<span class="user-status online">В сети</span>' : '<span class="user-status offline">Оффлайн</span>';

        function renderActionsMultibutton(u) {
            const accessOn = (u.access ?? 0) > 0;
            const banOrActLbl = accessOn ? "Деактивировать" : "Активировать";
            const kickItems = `
                <div class="dropdown-item" data-act="kick-vsupport" data-id="${u.id}">Отключить от V-Support</div>
                <div class="dropdown-item" data-act="kick-vdocs"    data-id="${u.id}">Отключить от V-DOCs</div>
                <div class="dropdown-item" data-act="kick-all"      data-id="${u.id}">Отключить везде</div>
            `;
            const adminItems = isAdmin ? `
                <div class="dropdown-item sep"></div>
                <div class="dropdown-item" data-act="role" data-role="BaseUser"     data-id="${u.id}">Переключить роль: BaseUser</div>
                <div class="dropdown-item" data-act="role" data-role="SupportAgent" data-id="${u.id}">Переключить роль: SupportAgent</div>
                <div class="dropdown-item" data-act="role" data-role="SupportAdmin" data-id="${u.id}">Переключить роль: SupportAdmin</div>
                <div class="dropdown-item sep"></div>
                <div class="dropdown-item" data-act="${accessOn ? "ban" : "activate"}" data-id="${u.id}">${banOrActLbl}</div>
            ` : "";
            return `
            <div class="multi-button compact action-multibutton" style="position:relative;"
                 data-id="${u.id}" data-access="${u.access ?? 0}">
              <button class="button-sliding primary compact action-button"
                      data-act="kick-vsupport" data-id="${u.id}">
                Отключить от V-Support
              </button>
              <div class="dropdown-arrow compact" tabindex="0">▾</div>
              <div class="action-dropdown-menu compact" style="display:none; position:absolute; min-width:200px;">
                ${kickItems}
                ${adminItems}
              </div>
            </div>`;
        }

        const rowsById = new Map();

        function rowHtml(u) {
            const roles = (u.roles || []).map((r) => badge(r)).join(" ") || "-";
            const accessOn = (u.access ?? 0) > 0;
            const access = accessOn ? '<span class="badge badge--ok">Активирована</span>' : '<span class="badge badge--blocked">Деактивирована</span>';
            const vsup = !!(u.presence?.VSupport?.online);
            const vdoc = !!(u.presence?.VDocs?.online);
            return `
              <tr data-id="${u.id}">
                <td>${u.userName ?? ""}</td>
                <td>${u.fullName ?? ""}</td>
                <td>${u.organization ?? ""}</td>
                <td>${roles}</td>
                <td>${statusPill(vdoc)}</td>
                <td>${statusPill(vsup)}</td>
                <td>${access}</td>
                <td>${u.lastEntry || ""}</td>
                <td>${renderActionsMultibutton(u)}</td>
              </tr>`;
        }

        function removePlaceholderRowIfAny() {
            const onlyRow = tbody.children.length === 1 ? tbody.firstElementChild : null;
            if (onlyRow && !onlyRow.getAttribute("data-id")) {
                tbody.removeChild(onlyRow);
            }
        }

        function upsertRow(u, toTop = false) {
            if (rowsById.has(u.id)) return;
            removePlaceholderRowIfAny();
            const tmp = document.createElement("tbody");
            tmp.innerHTML = rowHtml(u);
            const tr = tmp.firstElementChild;
            if (toTop && tbody.firstElementChild) {
                tbody.insertBefore(tr, tbody.firstElementChild);
            } else {
                tbody.appendChild(tr);
            }
            rowsById.set(u.id, tr);
            setupActionDropdownsFor(tr);
        }

        function renderRows(list) {
            rowsById.clear();
            if (!Array.isArray(list) || list.length === 0) {
                tbody.innerHTML = `<tr><td colspan="9">Ничего не найдено</td></tr>`;
                return;
            }
            const rows = list.map((u) => rowHtml(u));
            tbody.innerHTML = rows.join("");
            $$("#accountsTable tbody tr").forEach(tr => {
                const id = tr.getAttribute("data-id");
                if (id) rowsById.set(id, tr);
            });
            setupActionDropdowns();
        }

        function setIfChanged(el, html) { if (el && el.innerHTML !== html) el.innerHTML = html; }

        function patchRowCells(tr, patch) {
            const tds = tr ? tr.children : null;
            if (!tds || tds.length < 9) return;
            const rolesHtml = (Array.isArray(patch.roles) && patch.roles.length > 0) ? patch.roles.map(badge).join(" ") : "-";
            const vdocsHtml = statusPill(!!(patch.presence && patch.presence.VDocs && patch.presence.VDocs.online));
            const vsupHtml = statusPill(!!(patch.presence && patch.presence.VSupport && patch.presence.VSupport.online));
            const accessHtml = (patch.access ?? 0) > 0 ? '<span class="badge badge--ok">Активирована</span>' : '<span class="badge badge--blocked">Деактивирована</span>';
            const lastEntry = patch.lastEntry || "";
            setIfChanged(tds[3], rolesHtml);
            setIfChanged(tds[4], vdocsHtml);
            setIfChanged(tds[5], vsupHtml);
            setIfChanged(tds[6], accessHtml);
            setIfChanged(tds[7], lastEntry);
        }

        // dropdown (плавающее меню)
        let floatingMenu = null;
        let floatingCtx = null;

        function ensureFloatingMenu() {
            if (floatingMenu) return floatingMenu;
            floatingMenu = document.createElement("div");
            floatingMenu.id = "accounts-action-floating-menu";
            floatingMenu.className = "action-dropdown-menu compact";
            floatingMenu.style.display = "none";
            floatingMenu.style.minWidth = "200px";
            document.body.appendChild(floatingMenu);
            return floatingMenu;
        }
        function closeFloatingMenu() {
            if (!floatingMenu) return;
            floatingMenu.style.display = "none";
            floatingMenu.innerHTML = "";
            floatingCtx = null;
        }
        function openFloatingMenuFrom(dropdown) {
            const btn = dropdown.querySelector(".action-button");
            const arrow = dropdown.querySelector(".dropdown-arrow");
            const menu = dropdown.querySelector(".action-dropdown-menu");
            if (!btn || !arrow || !menu) return;
            if (floatingCtx?.dropdown === dropdown && floatingMenu?.style.display === "block") {
                closeFloatingMenu();
                return;
            }
            ensureFloatingMenu();
            floatingMenu.innerHTML = menu.innerHTML;
            floatingMenu.style.visibility = "hidden";
            floatingMenu.style.display = "block";
            floatingMenu.style.left = "-9999px";
            floatingMenu.style.top = "-9999px";
            const rect = arrow.getBoundingClientRect();
            const w = floatingMenu.offsetWidth;
            const h = floatingMenu.offsetHeight;
            let left = Math.min(Math.max(8, rect.right - w), window.innerWidth - w - 8);
            let topDown = rect.bottom + 4;
            let topUp = rect.top - 4 - h;
            let top = topDown + h <= window.innerHeight ? topDown : Math.max(8, topUp);
            floatingMenu.style.left = left + "px";
            floatingMenu.style.top = top + "px";
            floatingMenu.style.visibility = "visible";
            floatingMenu.querySelectorAll(".dropdown-item").forEach((item) => {
                item.addEventListener("click", async () => {
                    const act = item.dataset.act;
                    const id = item.dataset.id;
                    const role = item.dataset.role || "";
                    const btnEl = btn;
                    btnEl.textContent = item.textContent.trim();
                    btnEl.dataset.act = act;
                    if (role) btnEl.dataset.role = role; else btnEl.removeAttribute("data-role");
                    closeFloatingMenu();
                    await runAction(act, id, role, dropdown);
                });
            });
            floatingCtx = { dropdown };
            const onDocClick = (e) => { if (!floatingMenu.contains(e.target) && !dropdown.contains(e.target)) cleanup(); };
            const onAnyScroll = () => cleanup();
            const onResize = () => cleanup();
            function cleanup() {
                closeFloatingMenu();
                document.removeEventListener("click", onDocClick, true);
                window.removeEventListener("scroll", onAnyScroll, true);
                window.removeEventListener("resize", onResize, true);
            }
            document.addEventListener("click", onDocClick, true);
            window.addEventListener("scroll", onAnyScroll, true);
            window.addEventListener("resize", onResize, true);
        }

        function setupActionDropdowns() {
            $$("#accountsTable .action-multibutton").forEach((box) => {
                const btn = box.querySelector(".action-button");
                const arrow = box.querySelector(".dropdown-arrow");
                if (!btn || !arrow) return;
                arrow.onclick = (e) => { e.stopPropagation(); openFloatingMenuFrom(box); };
                btn.onclick = async () => {
                    const act = btn.dataset.act || "kick-vsupport";
                    const role = btn.dataset.role || "";
                    await runAction(act, box.dataset.id, role, box);
                };
            });
        }
        function setupActionDropdownsFor(tr) {
            const box = tr.querySelector(".action-multibutton");
            if (!box) return;
            const btn = box.querySelector(".action-button");
            const arrow = box.querySelector(".dropdown-arrow");
            if (!btn || !arrow) return;
            arrow.onclick = (e) => { e.stopPropagation(); openFloatingMenuFrom(box); };
            btn.onclick = async () => {
                const act = btn.dataset.act || "kick-vsupport";
                const role = btn.dataset.role || "";
                await runAction(act, box.dataset.id, role, box);
            };
        }

        async function runAction(act, id, role, _box) {
            try {
                if (act === "kick-vsupport") {
                    await post(`/api/support/accounts/${id}/kick`, { scope: "VSupport" });
                } else if (act === "kick-vdocs") {
                    await post(`/api/support/accounts/${id}/kick`, { scope: "VDocs" });
                } else if (act === "kick-all") {
                    await post(`/api/support/accounts/${id}/kick`, { scope: "All" });
                } else if (act === "role") {
                    if (!isAdmin || !role) return;
                    await post(`/api/support/accounts/${id}/set-role`, { role });
                } else if (act === "ban" || act === "activate") {
                    if (!isAdmin) return;
                    await post(`/api/support/accounts/${id}/toggle-access`, {});
                }
                await loadAccounts();
            } catch (e) {
                console.error("[accounts] action error", e);
                alert("Ошибка: " + e.message);
            }
        }

        function buildHeadAndFilters(orgs) {
            buildHead();
            populateOrgFilter(orgs);
        }

        let projects = [];
        let createdCursorMs = 0;
        let pollTimer = null;
        let pulseTimer = null;

        function stopPolling() {
            if (pollTimer) { clearInterval(pollTimer); pollTimer = null; }
            if (pulseTimer) { clearInterval(pulseTimer); pulseTimer = null; }
        }
        function restartPolling() {
            stopPolling();
            pollTimer = setInterval(pollDelta, 2000);
            pulseTimer = setInterval(pulseTick, 3000);
        }

        async function loadAccounts() {
            const role = roleSel.value;
            const org = orgSel.value;
            const query = searchEl.value?.trim() ?? "";
            const url = new URL("/api/support/accounts", location.origin);
            if (role) url.searchParams.set("role", role);
            if (org) url.searchParams.set("org", org);
            if (query) url.searchParams.set("q", query);
            tbody.innerHTML = `<tr><td>Загрузка…</td></tr>`;
            try {
                const raw = await getJson(url.toString());
                const { projects: projs, users, organizations } = normalizeAccountsResponse(raw);
                projects = projs;
                buildHeadAndFilters(organizations);
                renderRows(users);
                const maxCreated = users.reduce((m, u) => Math.max(m, Number(u.createdAtMs || 0)), 0);
                if (maxCreated > 0) createdCursorMs = Math.max(createdCursorMs, maxCreated + 1);
                restartPolling();
                await pulseTick();
            } catch (e) {
                console.error("[accounts] load error", e);
                tbody.innerHTML = `<tr><td style="color:#c33;">Ошибка загрузки</td></tr>`;
            }
        }

        async function pollDelta() {
            try {
                const role = roleSel.value;
                const org = orgSel.value;
                const query = searchEl.value?.trim() ?? "";
                const url = new URL("/api/support/accounts/delta", location.origin);
                url.searchParams.set("sinceMs", String(createdCursorMs || 0));
                if (role) url.searchParams.set("role", role);
                if (org) url.searchParams.set("org", org);
                if (query) url.searchParams.set("q", query);

                const res = await getJson(url.toString());
                const list = Array.isArray(res?.users) ? res.users : [];
                if (list.length > 0) {
                    list.sort((a, b) => Number(a.createdAtMs || 0) - Number(b.createdAtMs || 0));
                    for (const u of list) upsertRow(u, true);
                    const maxCreated = list.reduce((m, u) => Math.max(m, Number(u.createdAtMs || 0)), createdCursorMs);
                    if (maxCreated > 0) createdCursorMs = Math.max(createdCursorMs, maxCreated + 1);
                    await pulseTick();
                }
            } catch (e) {
                // молчим — периодический опрос, возможны гонки при навигации
                // console.debug("[accounts] delta poll error", e);
            }
        }

        async function pulseTick() {
            try {
                const ids = Array.from(rowsById.keys());
                if (ids.length === 0) return;
                const res = await post("/api/support/accounts/pulse", { ids });
                const list = Array.isArray(res?.users) ? res.users : [];
                for (const u of list) {
                    const tr = rowsById.get(u.id);
                    if (tr) patchRowCells(tr, u);
                }
            } catch (e) {
                // console.debug("[accounts] pulse error", e);
            }
        }

        roleSel.addEventListener("change", loadAccounts);
        orgSel.addEventListener("change", loadAccounts);
        let searchTimer = null;
        searchEl.addEventListener("input", () => {
            clearTimeout(searchTimer);
            searchTimer = setTimeout(loadAccounts, 250);
        });

        await loadAccounts();
    }

    window.initAccountsPage = init;
})();
