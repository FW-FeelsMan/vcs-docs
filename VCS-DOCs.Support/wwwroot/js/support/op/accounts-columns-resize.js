// wwwroot/js/support/op/accounts-columns-resize.js
// Accounts columns resize (stable, like grid-demo):
// - resize from header OR any body cell border
// - uses <colgroup><col style="width:..."> as source of truth
// - table.width is ALWAYS synced to sum(col widths) => right edge moves, no "compress others"
// - persists to localStorage
// - dx accounts for horizontal scroll changes

(() => {
    if (window.__accountsColumnsResizeInit) return;
    window.__accountsColumnsResizeInit = true;

    const TABLE_ID = "accountsTable";
    const ROOT_ID = "op-accounts";

    const STORAGE_KEY = "support.accountsTable.colWidths.v4";
    const MIN_COL_PX = 70;
    const MAX_COL_PX = 900;

    const GRIP_PX = 6;

    const clamp = (v, min, max) => Math.max(min, Math.min(max, v));

    const $ = (sel, root = document) => root.querySelector(sel);

    function ensureHoverCssOnce() {
        const id = "accountsResizeHoverCss";
        if (document.getElementById(id)) return;

        const s = document.createElement("style");
        s.id = id;
        s.textContent = `
            body.accounts-col-resize-hover { cursor: col-resize !important; }
            body.accounts-col-resize-hover * { cursor: col-resize !important; }

            body.accounts-col-resizing { cursor: col-resize !important; user-select: none !important; }
            body.accounts-col-resizing * { cursor: col-resize !important; user-select: none !important; }

            #${ROOT_ID} #${TABLE_ID} thead th { position: relative; }
            #${ROOT_ID} #${TABLE_ID} thead th:hover {
                box-shadow: inset -1px 0 0 rgba(90,155,213,.55);
            }
            body.accounts-col-resizing #${ROOT_ID} #${TABLE_ID} thead th {
                box-shadow: inset -1px 0 0 rgba(90,155,213,.75);
            }
        `;
        document.head.appendChild(s);
    }

    function getRoot() {
        return document.getElementById(ROOT_ID);
    }

    function getTable() {
        const root = getRoot();
        if (!root) return null;
        return $(`#${TABLE_ID}`, root);
    }

    function getScrollHost(root) {
        // твой контейнер с прокруткой
        return $(".content-scrollable", root) || root;
    }

    function getCols(table) {
        return table ? Array.from(table.querySelectorAll("colgroup col")) : [];
    }

    function readStored() {
        try {
            const raw = localStorage.getItem(STORAGE_KEY);
            if (!raw) return null;
            const obj = JSON.parse(raw);
            return obj && typeof obj === "object" ? obj : null;
        } catch {
            return null;
        }
    }

    function storeWidths(widths) {
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(widths)); } catch { }
    }

    function getColWidthPx(col, fallbackPx) {
        // ВАЖНО: читаем style.width первым (это наша истина)
        const m = (col.style.width || "").match(/(\d+)\s*px/i);
        if (m) return parseInt(m[1], 10);

        const w = parseFloat(getComputedStyle(col).width || "");
        if (isFinite(w) && w > 0) return Math.round(w);

        return fallbackPx;
    }

    function captureCurrentWidths(table) {
        const cols = getCols(table);
        const out = {};
        cols.forEach((col, idx) => {
            const w = getColWidthPx(col, 0);
            if (w > 0) out[idx] = w;
        });
        return out;
    }

    function sumColsPx(cols) {
        let total = 0;
        for (const c of cols) {
            const w = getColWidthPx(c, 0);
            if (w > 0) total += w;
        }
        return total;
    }

    // === KEY FIX ===
    // Table width follows SUM(col widths) always.
    function syncTableWidthToCols(table) {
        if (!table) return;
        const cols = getCols(table);
        if (!cols.length) return;

        const total = sumColsPx(cols);
        if (total > 0) {
            table.style.width = `${total}px`;
        } else {
            table.style.width = "auto";
        }
    }

    // throttle width sync during drag
    function makeRafSync() {
        let raf = 0;
        return (table) => {
            if (raf) return;
            raf = requestAnimationFrame(() => {
                raf = 0;
                syncTableWidthToCols(table);
            });
        };
    }
    const rafSync = makeRafSync();

    function applyStoredWidths(table) {
        const cols = getCols(table);
        const stored = readStored();
        if (!stored || !cols.length) return;

        cols.forEach((col, idx) => {
            const w = stored[idx];
            if (typeof w === "number" && isFinite(w) && w > 0) {
                col.style.width = `${clamp(Math.round(w), MIN_COL_PX, MAX_COL_PX)}px`;
            }
        });

        syncTableWidthToCols(table);
    }

    function findGripFromEvent(e, table) {
        const cell = e.target.closest?.("th, td");
        if (!cell) return null;

        const cols = getCols(table);
        if (!cols.length) return null;

        const idxCell = cell.cellIndex;
        if (!(idxCell >= 0 && idxCell < cols.length)) return null;

        const r = cell.getBoundingClientRect();
        const nearRight = r.right - e.clientX;
        const nearLeft = e.clientX - r.left;

        if (nearRight >= 0 && nearRight <= GRIP_PX) {
           /* if (idxCell === cols.length - 1) return null; // last col not resizable*/
            return idxCell;
        }

        if (nearLeft >= 0 && nearLeft <= GRIP_PX) {
            const idxPrev = idxCell - 1;
            if (idxPrev >= 0) return idxPrev;
        }

        return null;
    }

    function setHoverCursor(on) {
        document.body.classList.toggle("accounts-col-resize-hover", !!on);
    }

    function initDragging(root, table) {
        if (!table || table.__accountsColResizeBound) return;
        table.__accountsColResizeBound = true;

        const scrollHost = getScrollHost(root);

        let drag = null;

        const onMouseDown = (e) => {
            if (e.button !== 0) return;

            const idx = findGripFromEvent(e, table);
            if (idx == null) return;

            const cols = getCols(table);
            if (!(idx >= 0 && idx < cols.length)) return;

            e.preventDefault();

            const startW = clamp(
                Math.round(getColWidthPx(cols[idx], 120)),
                MIN_COL_PX,
                MAX_COL_PX
            );

            // учитываем горизонтальный scroll, чтобы dx не "дергался"
            const startScrollLeft = scrollHost?.scrollLeft || 0;
            const startX = e.clientX;

            drag = { idx, startX, startW, startScrollLeft };

            document.body.classList.add("accounts-col-resizing");
            setHoverCursor(true);
        };

        const onMouseMove = (e) => {
            if (drag) {
                const cols = getCols(table);
                if (!cols.length) return;

                const nowScrollLeft = scrollHost?.scrollLeft || 0;

                // dx в “мировых координатах” = clientX + scrollLeft
                const dx = (e.clientX + nowScrollLeft) - (drag.startX + drag.startScrollLeft);

                const newW = clamp(drag.startW + dx, MIN_COL_PX, MAX_COL_PX);
                cols[drag.idx].style.width = `${Math.round(newW)}px`;

                rafSync(table); // right edge moves smoothly
                return;
            }

            const idx = findGripFromEvent(e, table);
            setHoverCursor(idx != null);
        };

        const onMouseLeave = () => {
            if (!drag) setHoverCursor(false);
        };

        const onMouseUp = () => {
            if (!drag) return;

            document.body.classList.remove("accounts-col-resizing");
            setHoverCursor(false);

            syncTableWidthToCols(table);
            storeWidths(captureCurrentWidths(table));

            drag = null;
        };

        table.addEventListener("mousedown", onMouseDown);
        table.addEventListener("mousemove", onMouseMove);
        table.addEventListener("mouseleave", onMouseLeave);

        window.addEventListener("mousemove", onMouseMove);
        window.addEventListener("mouseup", onMouseUp);

        table.__accountsColResizeDispose = () => {
            try { table.removeEventListener("mousedown", onMouseDown); } catch { }
            try { table.removeEventListener("mousemove", onMouseMove); } catch { }
            try { table.removeEventListener("mouseleave", onMouseLeave); } catch { }
            try { window.removeEventListener("mousemove", onMouseMove); } catch { }
            try { window.removeEventListener("mouseup", onMouseUp); } catch { }
        };
    }

    function initOnce() {
        const root = getRoot();
        const table = getTable();
        if (!root || !table) return;

        ensureHoverCssOnce();

        // base: table width derived from current colgroup widths
        syncTableWidthToCols(table);

        // then override by saved widths (if any)
        applyStoredWidths(table);

        initDragging(root, table);
    }

    function boot() {
        if (!getRoot() || !getTable()) return;
        initOnce();
    }

    function watchAccountsHead() {
        const head = document.getElementById("accountsHead");
        if (!head) return;

        let t = null;
        const obs = new MutationObserver(() => {
            clearTimeout(t);
            t = setTimeout(() => {
                const table = getTable();
                if (!table) return;
                applyStoredWidths(table);
                syncTableWidthToCols(table);
            }, 50);
        });

        obs.observe(head, { childList: true, subtree: true });

        const root = getRoot();
        if (root) {
            root.__disposeColsResize = () => {
                try { obs.disconnect(); } catch { }
                const table = getTable();
                try { table?.__accountsColResizeDispose?.(); } catch { }
            };
        }
    }

    document.addEventListener("SupportContentChanged", () => setTimeout(() => {
        boot();
        watchAccountsHead();
    }, 0));

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", () => {
            boot();
            watchAccountsHead();
            setTimeout(boot, 50);
            setTimeout(boot, 250);
        });
    } else {
        boot();
        watchAccountsHead();
        setTimeout(boot, 50);
        setTimeout(boot, 250);
    }

    window.initAccountsColResize = initOnce;
})();
