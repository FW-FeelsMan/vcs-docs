console.log('storage-sortable.js loaded!');

(function () {
    'use strict';

    let savedColumnWidth = null;

    function initSortingInternal() {
        if (window.__storageSortingInitialized) return;
        window.__storageSortingInitialized = true;

        const table = document.getElementById('userFilesTable');
        if (!table) return;

        const tbody = table.querySelector('tbody');
        const headers = table.querySelectorAll('th');
        if (!tbody || !headers.length) return;

        console.log('Storage sorting initialized, headers:', headers.length);

        let currentSort = {
            index: 0,
            ascending: true,
            type: headers[0]?.dataset.type || 'string'
        };

        headers.forEach((header, idx) => {
            const type = header.dataset.type;
            if (!type) return;

            header.style.cursor = 'pointer';
            header.addEventListener('click', (e) => {
                const t = e.target;
                if (t && (t.classList?.contains('col-resizer') || t.classList?.contains('col-resizer-overlay') || t.classList?.contains('col-resizer-line'))) {
                    return;
                }

                if (currentSort.index === idx) {
                    currentSort.ascending = !currentSort.ascending;
                } else {
                    currentSort = { index: idx, ascending: true, type: type };
                }
                applySorting();
            });
        });

        function parseCustomDate(dateStr) {
            const parts = (dateStr || '').split(/[.\s:]/);
            if (parts.length < 3) return new Date(0);

            const day = parseInt(parts[0], 10);
            const month = parseInt(parts[1], 10) - 1;
            const year = parseInt(parts[2], 10);
            const hours = parts.length > 3 ? parseInt(parts[3], 10) : 0;
            const minutes = parts.length > 4 ? parseInt(parts[4], 10) : 0;
            const seconds = parts.length > 5 ? parseInt(parts[5], 10) : 0;

            return new Date(year, month, day, hours, minutes, seconds);
        }

        function applySorting() {
            const rows = Array.from(tbody.querySelectorAll('tr'));

            rows.sort((a, b) => {
                let x = a.children[currentSort.index]?.textContent.trim() || '';
                let y = b.children[currentSort.index]?.textContent.trim() || '';

                if (currentSort.type === 'number') {
                    x = parseFloat(x.replace(',', '.')) || 0;
                    y = parseFloat(y.replace(',', '.')) || 0;
                } else if (currentSort.type === 'date') {
                    x = parseCustomDate(x);
                    y = parseCustomDate(y);
                } else {
                    x = x.toLowerCase();
                    y = y.toLowerCase();
                }

                if (x === y) return 0;

                // Оставляю твою логику как была (по факту: "свежее/больше сверху")
                return currentSort.ascending
                    ? (x > y ? -1 : 1)
                    : (x < y ? -1 : 1);
            });

            headers.forEach(h => h.classList.remove('asc', 'desc'));
            headers[currentSort.index].classList.add(currentSort.ascending ? 'asc' : 'desc');

            rows.forEach(row => tbody.appendChild(row));
        }

        const initialIndex = Array.from(headers).findIndex(h => h.dataset.type === 'date');
        if (initialIndex !== -1) {
            currentSort.index = initialIndex;
            currentSort.type = headers[initialIndex].dataset.type;
            currentSort.ascending = true;
        }

        applySorting();

        window.reapplyStorageSort = function () {
            applySorting();
            if (savedColumnWidth) applyColumnWidth(table, savedColumnWidth);
            // после сортировки часто меняется ширина/лейаут — дёрнем ресайзер
            window.reinitResizer?.();
        };

        // Ресайзер: сразу + авто-догон на показ секции
        setTimeout(() => {
            initColumnResize(table, headers);
            hookStorageSectionVisibilityAuto();
        }, 0);
    }

    // -----------------------------
    // Resizer cleanup
    // -----------------------------
    function cleanupResizerState() {
        const st = window.__storageResizerState;
        if (!st) return;

        try { st.ro?.disconnect(); } catch { }
        try { st.mo?.disconnect(); } catch { }
        try { st.io?.disconnect(); } catch { }

        try { window.removeEventListener('resize', st.onResize); } catch { }

        try {
            (st.scrollHosts || []).forEach(h => {
                try { h.removeEventListener('scroll', st.onScroll); } catch { }
            });
        } catch { }

        try {
            (st.animHosts || []).forEach(h => {
                try {
                    h.removeEventListener('animationend', st.onAnimEnd);
                    h.removeEventListener('transitionend', st.onAnimEnd);
                } catch { }
            });
        } catch { }

        try { st.resizer?.remove(); } catch { }

        window.__storageResizerState = null;
    }

    // -----------------------------
    // Resizer init (single active handle)
    // -----------------------------
    function initColumnResize(table, headers) {
        const firstHeader = headers[0];
        if (!firstHeader) return;

        cleanupResizerState();

        const wrapper = table.closest('.files-table-wrap');
        if (!wrapper) {
            console.error('Table wrapper not found');
            return;
        }

        wrapper.style.position = 'relative';

        const resizer = document.createElement('div');
        resizer.className = 'col-resizer-overlay';
        resizer.setAttribute('aria-hidden', 'true');

        const line = document.createElement('div');
        line.className = 'col-resizer-line';
        resizer.appendChild(line);

        wrapper.appendChild(resizer);

        let rafId = 0;

        // viewport по Y: у тебя часто скроллит .content-scrollable, а не wrapper
        const viewport = wrapper.closest('.content-scrollable') || wrapper;

        function computeVisibleSegment() {
            const wrapperRect = wrapper.getBoundingClientRect();
            const viewportRect = viewport.getBoundingClientRect();

            const visibleTop = Math.max(wrapperRect.top, viewportRect.top);
            const visibleBottom = Math.min(wrapperRect.bottom, viewportRect.bottom);

            // clamp
            const rawTopLocal = visibleTop - wrapperRect.top;
            const topLocal = Math.max(0, Math.min(rawTopLocal, wrapperRect.height));

            const rawHeight = visibleBottom - visibleTop;
            const height = Math.max(0, Math.min(rawHeight, wrapperRect.height - topLocal));

            return { height, topLocal };
        }


        // ВАЖНО: X считаем по layout-координатам (offsetLeft/offsetWidth),
        // чтобы не зависеть от промежуточных transform/анимаций/подгрузок.
        function calcLeftPx() {
            const w = firstHeader.offsetWidth;
            if (!w || w <= 0) return null;

            // offsetLeft для TH в таблице нормальный (layout координаты)
            const left = (firstHeader.offsetLeft + w) - wrapper.scrollLeft - 7;
            if (!Number.isFinite(left) || left < 0) return null;

            return left;
        }

        function updatePosition() {
            // если секция скрыта/ещё не в лэйауте
            if (wrapper.offsetWidth <= 0 || wrapper.offsetHeight <= 0) return false;

            const leftPx = calcLeftPx();
            if (leftPx === null) return false;

            const { height, topLocal } = computeVisibleSegment();
            if (height <= 4) return false;

            resizer.style.position = 'absolute';
            resizer.style.left = `${leftPx}px`;
            resizer.style.top = `${topLocal}px`;
            resizer.style.width = `14px`;
            resizer.style.height = `${height}px`;
            resizer.style.cursor = 'col-resize';
            resizer.style.zIndex = '999';
            resizer.style.userSelect = 'none';
            resizer.style.pointerEvents = 'auto';
            resizer.style.background = 'transparent';

            line.style.position = 'absolute';
            line.style.left = '50%';
            line.style.top = '0';
            line.style.transform = 'translateX(-50%)';
            line.style.width = '2px';
            line.style.height = '100%';
            line.style.borderRadius = '999px';
            line.style.background = window.__storageResizerState?.isResizing
                ? 'rgba(90, 155, 213, 0.85)'
                : 'rgba(17, 24, 39, 0.18)';

            return true;
        }

        function scheduleUpdate() {
            if (rafId) cancelAnimationFrame(rafId);
            rafId = requestAnimationFrame(() => {
                updatePosition();
            });
        }

        // 1) Первый апдейт
        scheduleUpdate();

        // 2) “settle-loop” — первые ~1.1с поджимаем позицию,
        // чтобы пережить анимации/подгрузку строк/шрифты.
        const settleStart = performance.now();
        const SETTLE_MS = 1100;

        function settleTick() {
            scheduleUpdate();
            if (performance.now() - settleStart < SETTLE_MS) {
                requestAnimationFrame(settleTick);
            }
        }
        requestAnimationFrame(settleTick);

        // 3) После полной загрузки страницы/шрифтов — ещё раз
        window.addEventListener('load', scheduleUpdate, { once: true });
        if (document.fonts && document.fonts.ready) {
            document.fonts.ready.then(() => scheduleUpdate()).catch(() => { });
        }

        // hover
        resizer.addEventListener('mouseenter', () => {
            if (!window.__storageResizerState?.isResizing) {
                line.style.background = 'rgba(90, 155, 213, 0.55)';
            }
        });
        resizer.addEventListener('mouseleave', () => {
            if (!window.__storageResizerState?.isResizing) {
                line.style.background = 'rgba(17, 24, 39, 0.18)';
            }
        });

        // scroll hosts: по X может быть wrapper, по Y часто viewport
        const scrollHosts = [];
        if (wrapper) scrollHosts.push(wrapper);
        if (viewport && viewport !== wrapper) scrollHosts.push(viewport);

        const onScroll = () => scheduleUpdate();
        scrollHosts.forEach(h => h.addEventListener('scroll', onScroll, { passive: true }));

        const onResize = () => scheduleUpdate();
        window.addEventListener('resize', onResize);

        // ResizeObserver: если меняется ширина/высота
        const ro = new ResizeObserver(() => scheduleUpdate());
        ro.observe(wrapper);
        ro.observe(table);
        ro.observe(firstHeader);
        ro.observe(viewport);

        // MutationObserver: наблюдаем ЗА ТАБЛИЦЕЙ ЦЕЛИКОМ (на случай пересоздания tbody)
        const mo = new MutationObserver(() => scheduleUpdate());
        mo.observe(table, { childList: true, subtree: true });

        // IntersectionObserver: когда реально попали в viewport
        const io = new IntersectionObserver((entries) => {
            if (entries.some(e => e.isIntersecting)) scheduleUpdate();
        }, { threshold: 0.01 });
        io.observe(wrapper);

        // Если вокруг есть анимации/переходы — после их завершения позиционируем снова
        const storageSection = document.getElementById('storage');
        const animHosts = [
            document.querySelector('.profile_background'),
            document.querySelector('.profile-container'),
            storageSection
        ].filter(Boolean);

        const onAnimEnd = () => {
            // небольшой таймаут, чтобы браузер успел применить финальный layout
            setTimeout(() => scheduleUpdate(), 0);
        };

        animHosts.forEach(h => {
            h.addEventListener('animationend', onAnimEnd);
            h.addEventListener('transitionend', onAnimEnd);
        });

        // Drag
        let startX = 0;
        let startWidth = 0;

        resizer.addEventListener('mousedown', function (e) {
            e.preventDefault();
            e.stopPropagation();

            window.__storageResizerState.isResizing = true;

            startX = e.pageX;
            startWidth = firstHeader.offsetWidth || 0;

            document.body.classList.add('table-resizing');
            line.style.background = 'rgba(90, 155, 213, 0.85)';

            document.addEventListener('mousemove', handleMouseMove);
            document.addEventListener('mouseup', handleMouseUp);
        });

        function handleMouseMove(e) {
            if (!window.__storageResizerState?.isResizing) return;

            const diff = e.pageX - startX;
            const newWidth = Math.max(120, startWidth + diff);

            savedColumnWidth = newWidth;
            applyColumnWidth(table, newWidth);
            table.classList.add('user-resized');

            scheduleUpdate();
        }

        function handleMouseUp() {
            if (!window.__storageResizerState?.isResizing) return;

            window.__storageResizerState.isResizing = false;

            document.body.classList.remove('table-resizing');
            line.style.background = 'rgba(17, 24, 39, 0.18)';

            document.removeEventListener('mousemove', handleMouseMove);
            document.removeEventListener('mouseup', handleMouseUp);

            // финальная позиция
            scheduleUpdate();
        }

        window.__storageResizerState = {
            resizer,
            ro,
            mo,
            io,
            onScroll,
            onResize,
            scrollHosts,
            animHosts,
            onAnimEnd,
            isResizing: false
        };

        console.log('Resizer overlay created (single active handle, settled)');
    }

    function applyColumnWidth(table, width) {
        const firstHeader = table.querySelector('thead th:first-child');
        if (firstHeader) {
            firstHeader.style.width = width + 'px';
            firstHeader.style.minWidth = width + 'px';
            firstHeader.style.maxWidth = width + 'px';
        }

        const firstCells = table.querySelectorAll('tbody tr td:first-child');
        firstCells.forEach(cell => {
            cell.style.width = width + 'px';
            cell.style.minWidth = width + 'px';
            cell.style.maxWidth = width + 'px';
        });
    }

    window.applyStorageColumnWidth = function () {
        const table = document.getElementById('userFilesTable');
        if (table && savedColumnWidth) applyColumnWidth(table, savedColumnWidth);
    };

    window.reinitResizer = function () {
        const table = document.getElementById('userFilesTable');
        const headers = table?.querySelectorAll('th');
        if (table && headers && headers.length) {
            initColumnResize(table, headers);
            if (savedColumnWidth) applyColumnWidth(table, savedColumnWidth);
        }
    };

    // авто: когда секция storage становится активной (SPA)
    function hookStorageSectionVisibilityAuto() {
        if (window.__storageResizerVisibilityHooked) return;
        window.__storageResizerVisibilityHooked = true;

        const storageSection = document.getElementById('storage');
        if (!storageSection) return;

        const mo = new MutationObserver(() => {
            const isActive = storageSection.classList.contains('active') || storageSection.style.display !== 'none';
            if (!isActive) return;

            requestAnimationFrame(() => {
                window.reinitResizer?.();
            });
        });

        mo.observe(storageSection, { attributes: true, attributeFilter: ['class', 'style'] });
    }

    window.initStorageSorting = window.initStorageSorting || initSortingInternal;

})();
