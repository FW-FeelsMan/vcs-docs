// wwwroot/js/support/op/my-work-columns-resize.js
// "Мной закрытые заявки" (operator) columns resize (stable, like open/close tickets):
// - resize from header OR any body cell border
// - uses <colgroup><col style="width:..."> as source of truth
// - table.width is ALWAYS synced to sum(col widths) => right edge moves, no "compress others"
// - persists to localStorage
// - dx accounts for horizontal scroll changes
// - last column resizable (enabled)

(() => {
    if (window.__opMyWorkColResizeInit) return;
    window.__opMyWorkColResizeInit = true;

    const ROOT_ID = "op-my-work";
    const TABLE_ID = "opMyWorkTable";

    const STORAGE_KEY = "opMyWorkTable.colWidths.v1";
    const MIN_COL_PX = 70;
    const MAX_COL_PX = 900;
    const GRIP_PX = 6;

    const clamp = (v, min, max) => Math.max(min, Math.min(max, v));
    const $ = (sel, root = document) => root.querySelector(sel);

    function ensureHoverCssOnce() {
        const id = "opTicketsResizeHoverCss_v2";
        if (document.getElementById(id)) return;

        const s = document.createElement("style");
        s.id = id;
        s.textContent = `
            body.tickets-col-resize-hover { cursor: col-resize !important; }
            body.tickets-col-resize-hover * { cursor: col-resize !important; }

            body.tickets-col-resizing { cursor: col-resize !important; user-select: none !important; }
            body.tickets-col-resizing * { cursor: col-resize !important; user-select: none !important; }

            #${ROOT_ID} #${TABLE_ID} thead th { position: relative; }
            #${ROOT_ID} #${TABLE_ID} thead th:hover { box-shadow: inset -1px 0 0 rgba(90,155,213,.65); }
            body.tickets-col-resizing #${ROOT_ID} #${TABLE_ID} thead th { box-shadow: inset -1px 0 0 rgba(90,155,213,.85); }

            #${ROOT_ID} #${TABLE_ID} thead th .col-resizer {
                position: absolute;
                top: 0;
                right: -6px;
                width: 12px;
                height: 100%;
                cursor: col-resize;
                z-index: 10;
                touch-action: none;
                background: transparent;
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

    function getFallbackWidthFromHeader(table, idx) {
        try {
            const ths = table.querySelectorAll("thead th");
            const th = ths?.[idx];
            const w = th?.getBoundingClientRect?.().width;
            if (isFinite(w) && w > 0) return Math.round(w);
        } catch { }
        return 0;
    }

    function getColWidthPx(col, fallbackPx, table, idx) {
        const m = (col.style.width || "").match(/(\d+)\s*px/i);
        if (m) return parseInt(m[1], 10);

        const w1 = parseFloat(getComputedStyle(col).width || "");
        if (isFinite(w1) && w1 > 0) return Math.round(w1);

        const w2 = getFallbackWidthFromHeader(table, idx);
        if (w2 > 0) return w2;

        return fallbackPx;
    }

    function captureCurrentWidths(table) {
        const cols = getCols(table);
        const out = {};
        cols.forEach((col, idx) => {
            const w = getColWidthPx(col, 0, table, idx);
            if (w > 0) out[idx] = w;
        });
        return out;
    }

    function ensureExplicitColWidths(table) {
        const cols = getCols(table);
        if (!cols.length) return;

        cols.forEach((col, idx) => {
            const hasPx = /px/i.test(col.style.width || "");
            if (hasPx) return;

            const w = getColWidthPx(col, 0, table, idx);
            if (w > 0) col.style.width = `${clamp(w, MIN_COL_PX, MAX_COL_PX)}px`;
        });
    }

    function sumColsPx(table) {
        const cols = getCols(table);
        let total = 0;
        cols.forEach((c, idx) => {
            const w = getColWidthPx(c, 0, table, idx);
            if (w > 0) total += w;
        });
        return total;
    }

    function syncTableWidthToCols(table) {
        if (!table) return;
        const total = sumColsPx(table);
        if (total > 0) table.style.width = `${total}px`;
        else table.style.width = "auto";
    }

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
        if (!cols.length) return;

        if (stored) {
            cols.forEach((col, idx) => {
                const w = stored[idx];
                if (typeof w === "number" && isFinite(w) && w > 0) {
                    col.style.width = `${clamp(Math.round(w), MIN_COL_PX, MAX_COL_PX)}px`;
                }
            });
        }
    }

    function ensureResizers(table) {
        const ths = Array.from(table.querySelectorAll("thead th"));
        const cols = getCols(table);
        if (!ths.length || !cols.length) return;

        ths.forEach((th, idx) => {
            if (idx >= cols.length) return;
            if (th.querySelector(".col-resizer")) return;

            const grip = document.createElement("span");
            grip.className = "col-resizer";
            grip.dataset.colIndex = String(idx);
            grip.title = "Потяните, чтобы изменить ширину";
            th.appendChild(grip);
        });
    }

    function findGripFromEvent(e, table) {
        const handle = e.target.closest?.(".col-resizer");
        if (handle) {
            const idx = parseInt(handle.dataset.colIndex || "-1", 10);
            return Number.isFinite(idx) ? idx : null;
        }

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
            return idxCell; // last allowed
        }

        if (nearLeft >= 0 && nearLeft <= GRIP_PX) {
            const idxPrev = idxCell - 1;
            if (idxPrev >= 0) return idxPrev;
        }

        return null;
    }

    function setHoverCursor(on) {
        document.body.classList.toggle("tickets-col-resize-hover", !!on);
    }

    function initDragging(root, table) {
        if (!table || table.__ticketsColResizeBound) return;
        table.__ticketsColResizeBound = true;

        const scrollHost = getScrollHost(root);
        let drag = null;

        const onMouseDown = (e) => {
            if (e.button !== 0) return;

            const idx = findGripFromEvent(e, table);
            if (idx == null) return;

            const cols = getCols(table);
            if (!(idx >= 0 && idx < cols.length)) return;

            e.preventDefault();

            const startW = clamp(Math.round(getColWidthPx(cols[idx], 120, table, idx)), MIN_COL_PX, MAX_COL_PX);

            const startScrollLeft = scrollHost?.scrollLeft || 0;
            const startX = e.clientX;

            drag = { idx, startX, startW, startScrollLeft };

            document.body.classList.add("tickets-col-resizing");
            setHoverCursor(true);
        };

        const onMouseMove = (e) => {
            if (drag) {
                const cols = getCols(table);
                if (!cols.length) return;

                const nowScrollLeft = scrollHost?.scrollLeft || 0;
                const dx = (e.clientX + nowScrollLeft) - (drag.startX + drag.startScrollLeft);

                const newW = clamp(drag.startW + dx, MIN_COL_PX, MAX_COL_PX);
                cols[drag.idx].style.width = `${Math.round(newW)}px`;

                rafSync(table);
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

            document.body.classList.remove("tickets-col-resizing");
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

        table.__ticketsColResizeDispose = () => {
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

        // 1) freeze initial widths into px (important when CSS uses clamp/%)
        ensureExplicitColWidths(table);

        // 2) apply saved widths
        applyStoredWidths(table);

        // 3) make sure all cols are explicit px after apply
        ensureExplicitColWidths(table);

        // 4) sync table width to sum(cols)
        syncTableWidthToCols(table);

        // 5) header grips + drag
        ensureResizers(table);
        initDragging(root, table);

        // 6) if header rerenders dynamically — re-add grips and re-sync
        if (!table.__ticketsHeadObserver) {
            const thead = table.querySelector("thead");
            if (thead) {
                const obs = new MutationObserver(() => {
                    ensureResizers(table);
                    syncTableWidthToCols(table);
                });
                obs.observe(thead, { childList: true, subtree: true });
                table.__ticketsHeadObserver = obs;
            }
        }
    }

    function boot() {
        initOnce();
    }

    document.addEventListener("SupportContentChanged", () => setTimeout(boot, 0));

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", () => {
            boot();
            setTimeout(boot, 50);
            setTimeout(boot, 250);
        });
    } else {
        boot();
        setTimeout(boot, 50);
        setTimeout(boot, 250);
    }

    window.initOpMyWorkColResize = initOnce;
})();
