(function initUserFilesExcelResize() {
    if (window.__userFilesExcelResizeInit) return;
    window.__userFilesExcelResizeInit = true;

    function getScale() {
        const raw = getComputedStyle(document.documentElement).getPropertyValue("--ui-scale").trim();
        const v = parseFloat(raw);
        return Number.isFinite(v) && v > 0 ? v : 1;
    }

    function setCol1Width(table, px) {
        table.style.setProperty("--col1-w", px + "px");
    }

    function ensureResizers(table) {
        // УБИРАЕМ ресайзер из ШАПКИ (если он там уже был)
        table.querySelectorAll("thead .col-resizer").forEach(x => x.remove());

        // Добавляем ресайзер ТОЛЬКО в ПЕРВЫЙ столбец ТЕЛА таблицы (tbody)
        const bodies = Array.from(table.tBodies || []);
        bodies.forEach(tb => {
            const rows = tb.querySelectorAll("tr");
            rows.forEach(row => {
                const firstCell = row.cells[0];
                if (firstCell && !firstCell.querySelector(".col-resizer")) {
                    const r = document.createElement("span");
                    r.className = "col-resizer";
                    firstCell.appendChild(r);
                }
            });
        });
    }

    function attach(table) {
        if (table.__excelResizeAttached) return;
        table.__excelResizeAttached = true;

        ensureResizers(table);

        let startX = 0;
        let startW = 0;

        function onMove(e) {
            const scale = getScale();
            const minW = Math.round(150 * scale);
            const maxW = Math.round(1200 * scale);
            const dx = e.pageX - startX;
            setCol1Width(table, Math.max(minW, Math.min(maxW, startW + dx)));
        }

        function onUp() {
            document.removeEventListener("mousemove", onMove);
            document.removeEventListener("mouseup", onUp);
        }

        table.addEventListener("mousedown", (e) => {
            if (!e.target.classList.contains("col-resizer")) return;
            e.preventDefault();

            startX = e.pageX;
            const headCell = table.querySelector("thead th:nth-child(1)");
            startW = headCell.offsetWidth;

            document.addEventListener("mousemove", onMove);
            document.addEventListener("mouseup", onUp);
        });

        // Следим за появлением новых строк, чтобы добавить в них ресайзеры
        const mo = new MutationObserver(() => ensureResizers(table));
        Array.from(table.tBodies || []).forEach(tb => {
            mo.observe(tb, { childList: true });
        });
    }

    function tryInit() {
        const table = document.getElementById("userFilesTable");
        if (table) attach(table);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", tryInit);
    } else {
        tryInit();
    }
})();
