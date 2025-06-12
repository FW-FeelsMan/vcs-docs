window.initStorageSorting = function () {
    if (window.__storageSortingInitialized) return;
    window.__storageSortingInitialized = true;

    console.log('[storage-sortable] Инициализация сортировки');

    const table = document.getElementById("userFilesTable");
    const tbody = table.querySelector("tbody");
    const headers = table.querySelectorAll("th");

    let currentSort = {
        index: 0,
        ascending: true,
        type: headers[0]?.dataset.type || 'string'
    };

    headers.forEach((header, idx) => {
        const type = header.dataset.type;
        if (!type) return;

        header.style.cursor = "pointer";
        header.addEventListener("click", () => {
            if (currentSort.index === idx) {
                currentSort.ascending = !currentSort.ascending;
            } else {
                currentSort = {
                    index: idx,
                    ascending: true,
                    type: type
                };
            }
            applySorting();
        });
    });

    function parseCustomDate(dateStr) {
        const parts = dateStr.split(".");
        if (parts.length !== 3) return new Date(0);
        const [day, month, year] = parts;
        const fullYear = parseInt(year, 10);
        return new Date(fullYear < 100 ? 2000 + fullYear : fullYear, parseInt(month, 10) - 1, parseInt(day, 10));
    }

    function applySorting() {
        const rows = Array.from(tbody.querySelectorAll("tr"));

        rows.sort((a, b) => {
            let x = a.children[currentSort.index]?.textContent.trim() || "";
            let y = b.children[currentSort.index]?.textContent.trim() || "";

            if (currentSort.type === "number") {
                x = parseFloat(x.replace(",", ".")) || 0;
                y = parseFloat(y.replace(",", ".")) || 0;
            } else if (currentSort.type === "date") {
                x = parseCustomDate(x);
                y = parseCustomDate(y);
            } else {
                x = x.toLowerCase();
                y = y.toLowerCase();
            }

            if (x === y) return 0;
            return currentSort.ascending
                ? x > y ? -1 : 1
                : x < y ? -1 : 1;
        });

        headers.forEach(h => h.classList.remove("asc", "desc"));
        headers[currentSort.index].classList.add(currentSort.ascending ? "asc" : "desc");

        rows.forEach(row => tbody.appendChild(row));
    }

    const initialIndex = Array.from(headers).findIndex(h => h.dataset.type === "date");
    if (initialIndex !== -1) {
        currentSort.index = initialIndex;
        currentSort.type = headers[initialIndex].dataset.type;
        currentSort.ascending = true;
    }

    applySorting();

    window.reapplyStorageSort = function () {
        applySorting();
    };
};
