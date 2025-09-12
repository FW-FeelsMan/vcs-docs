(function () {
    function uniqSorted(arr) {
        return Array.from(new Set(arr)).filter(Boolean).sort((a, b) => a.localeCompare(b, 'ru'));
    }

    window.initAllOpenUserTickets = function (panel) {
        if (panel.__op_open_inited) return;
        panel.__op_open_inited = true;

        const root = panel.querySelector("#op-open-tickets") || panel;
        const tbody = root.querySelector("#opTicketsBody");
        const table = root.querySelector("#opTicketsTable");
        const searchBox = root.querySelector("#op_searchBox");
        const btnSearch = root.querySelector("#btn-op-search");
        const scopeTabs = root.querySelector("#scopeTabs");
        const orgSel = root.querySelector("#op_orgFilter");

        let currentScope = "all";
        let currentOrg = "";
        let currentQuery = "";
        let debounce = null;

        function buildOrgFilterFromTable() {
            if (!orgSel || !tbody) return;
            const orgs = uniqSorted(Array.from(tbody.querySelectorAll("tr td:nth-child(4)"))
                .map(td => (td.textContent || "").trim()));
            const current = orgSel.value;
            orgSel.innerHTML = `<option value="">Все организации</option>` +
                orgs.map(o => `<option value="${o}">${o}</option>`).join("");
            if (orgs.includes(current)) orgSel.value = current;
        }

        function getActiveScope() {
            const active = root.querySelector("#scopeTabs .seg-btn.is-active");
            return active ? (active.getAttribute("data-scope") || "all") : "all";
        }

        function applyFilter() {
            const rows = Array.from(tbody?.rows || []);
            const q = currentQuery.toLowerCase();
            const scope = currentScope;
            const org = currentOrg;

            rows.forEach(tr => {
                const txt = (tr.textContent || "").toLowerCase();
                const trOrg = (tr.cells[3]?.textContent || "").trim();
                const operator = (tr.getAttribute("data-operator") || "").trim();

                let ok = true;
                if (q) ok = txt.includes(q);
                if (ok && org) ok = trOrg === org;
                if (ok && scope === "mine") ok = operator.length > 0;
                if (ok && scope === "unassigned") ok = operator.length === 0;

                tr.style.display = ok ? "" : "none";
            });
        }

        function wireEvents() {
            scopeTabs?.addEventListener("click", onTabsClick);
            orgSel?.addEventListener("change", onOrgChange);
            btnSearch?.addEventListener("click", onSearchClick);
            searchBox?.addEventListener("input", onSearchInput);
            table?.addEventListener("click", onTableClick);
        }

        function unwireEvents() {
            try { scopeTabs?.removeEventListener("click", onTabsClick); } catch { }
            try { orgSel?.removeEventListener("change", onOrgChange); } catch { }
            try { btnSearch?.removeEventListener("click", onSearchClick); } catch { }
            try { searchBox?.removeEventListener("input", onSearchInput); } catch { }
            try { table?.removeEventListener("click", onTableClick); } catch { }
            try { clearTimeout(debounce); } catch { }
        }

        function onTabsClick(e) {
            const btn = e.target.closest(".seg-btn");
            if (!btn) return;
            scopeTabs.querySelectorAll(".seg-btn").forEach(b => b.classList.remove("is-active"));
            btn.classList.add("is-active");
            currentScope = getActiveScope();
            applyFilter();
        }

        function onOrgChange() {
            currentOrg = orgSel.value || "";
            applyFilter();
        }

        function onSearchClick() {
            currentQuery = (searchBox.value || "").trim();
            applyFilter();
        }

        function onSearchInput() {
            clearTimeout(debounce);
            debounce = setTimeout(() => {
                currentQuery = (searchBox.value || "").trim();
                applyFilter();
            }, 250);
        }

        function onTableClick(e) {
            const btn = e.target.closest(".btn-open");
            if (!btn) return;
            const tr = btn.closest("tr");
            const id = tr?.getAttribute("data-id");
            if (id) console.log("[open-tickets] open ticket:", id);
        }

        buildOrgFilterFromTable();
        currentScope = getActiveScope();
        applyFilter();
        wireEvents();

        panel.__dispose = function () {
            unwireEvents();
        };
    };
})();
