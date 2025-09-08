(() => {
    "use strict";

    // Где показываем подсказки:
    const TARGET_SELECTOR = 'table.file-table td, table.file-table th, .tt-auto';

    let bubble = null;
    let activeEl = null;
    let moveHandler = null;

    function ensureBubble() {
        if (bubble) return bubble;
        bubble = document.createElement('div');
        bubble.id = 'auto-tooltip';
        // если стилей нет — дадим базовые
        if (!document.getElementById('auto-tooltip-style')) {
            const st = document.createElement('style');
            st.id = 'auto-tooltip-style';
            st.textContent = `
#auto-tooltip{
  position:fixed;
  max-width:480px;
  padding:6px 8px;
  background:#111;
  color:#fff;
  font-size:13px;
  line-height:1.3;
  border-radius:6px;
  box-shadow:0 4px 16px rgba(0,0,0,.25);
  z-index:2147483647;
  pointer-events:none;
  opacity:0;
  transform:translate3d(0,0,0);
  transition:opacity .06s ease-out;
}
#auto-tooltip.on{ opacity:1; }
`;
            document.head.appendChild(st);
        }
        document.body.appendChild(bubble);
        return bubble;
    }

    function isOverflow(el) {
        if (!el) return false;
        return (el.scrollWidth - el.clientWidth > 1) ||
            (el.scrollHeight - el.clientHeight > 1);
    }

    function normalizeText(s) {
        return (s || "").replace(/\s+/g, ' ').trim();
    }

    function getTipText(el) {
        return normalizeText(
            el.getAttribute('data-tt') ??
            el.getAttribute('data-tt-text') ??
            el.textContent
        );
    }

    function positionNearCursor(x, y) {
        const tip = ensureBubble();

        // показать невидимо, чтобы измерить
        tip.classList.add('on');
        tip.style.visibility = 'hidden';
        tip.style.left = '-9999px';
        tip.style.top = '-9999px';

        const w = tip.offsetWidth;
        const h = tip.offsetHeight;

        // отступы от курсора и краёв экрана
        const pad = 8;      // отступ от краёв окна
        const dx = 12;      // смещение по X от курсора
        const dy = 16;      // смещение по Y от курсора

        let left = x + dx;
        if (left + w + pad > window.innerWidth) {
            left = Math.max(pad, x - w - dx);
        }

        let top = y + dy;
        if (top + h + pad > window.innerHeight) {
            top = Math.max(pad, y - h - dy);
        }

        tip.style.left = left + 'px';
        tip.style.top = top + 'px';
        tip.style.visibility = 'visible';
    }

    function positionNearElement(el) {
        const r = el.getBoundingClientRect();
        positionNearCursor(r.left + Math.min(24, r.width / 2), r.top + Math.min(24, r.height / 2));
    }

    function show(el, evt) {
        const text = getTipText(el);
        if (!text) return;

        const tip = ensureBubble();
        tip.textContent = text;
        activeEl = el;

        if (moveHandler) {
            document.removeEventListener('mousemove', moveHandler, true);
            moveHandler = null;
        }

        // двигаем тултип, пока мышь над тем же элементом
        moveHandler = (ev) => {
            if (!activeEl) return;
            const t = ev.target;
            if (t === activeEl || (activeEl.contains && activeEl.contains(t))) {
                positionNearCursor(ev.clientX, ev.clientY);
            } else {
                hide(); // ушли — спрятать
            }
        };
        document.addEventListener('mousemove', moveHandler, true);

        if (evt && typeof evt.clientX === 'number') {
            positionNearCursor(evt.clientX, evt.clientY);
        } else {
            // клавиатурный фокус, либо первое появление без движения
            positionNearElement(el);
        }
    }

    function hide() {
        if (!bubble) return;
        bubble.classList.remove('on');
        bubble.style.left = '-9999px';
        bubble.style.top = '-9999px';
        activeEl = null;
        if (moveHandler) {
            document.removeEventListener('mousemove', moveHandler, true);
            moveHandler = null;
        }
    }

    function findTarget(start) {
        let el = start;
        while (el && el !== document.documentElement) {
            if (el.matches && el.matches(TARGET_SELECTOR)) return el;
            el = el.parentElement;
        }
        return null;
    }

    // Показываем при наведении (если текст реально обрезан или указали data-tt)
    document.addEventListener('mouseenter', (e) => {
        const el = findTarget(e.target);
        if (!el || el.hasAttribute('data-tt-off')) return;
        if (el.hasAttribute('data-tt') || isOverflow(el)) show(el, e);
    }, true);

    // Прячем при уходе
    document.addEventListener('mouseleave', (e) => {
        if (!activeEl) return;
        const el = findTarget(e.target);
        if (el === activeEl || (el && el.contains(activeEl))) hide();
        else if (e.target === activeEl) hide();
    }, true);

    // Прячем при скролле/resize/escape
    window.addEventListener('scroll', () => activeEl && hide(), true);
    window.addEventListener('resize', () => activeEl && hide(), true);
    document.addEventListener('keydown', (e) => { if (e.key === 'Escape') hide(); });

    // Доступность: по TAB тоже покажем (рядом с элементом)
    document.addEventListener('focusin', (e) => {
        const el = findTarget(e.target);
        if (!el || el.hasAttribute('data-tt-off')) return;
        if (el.hasAttribute('data-tt') || isOverflow(el)) show(el, null);
    });
    document.addEventListener('focusout', (e) => {
        if (activeEl && (e.target === activeEl || activeEl.contains(e.target))) hide();
    });
})();
