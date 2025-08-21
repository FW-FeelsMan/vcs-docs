(() => {
    // ===== Тема (сохранение в localStorage) =====
    const root = document.documentElement;
    const key = "sdesk-theme";
    const btn = document.getElementById("sdesk-theme");
    const saved = localStorage.getItem(key);
    if (saved) root.setAttribute("data-theme", saved);
    btn?.addEventListener("click", () => {
        const next = root.getAttribute("data-theme") === "dark" ? "light" : "dark";
        root.setAttribute("data-theme", next);
        localStorage.setItem(key, next);
    });

    // ===== Навигация / загрузка контента с плавной анимацией =====
    const loader = document.getElementById("sdesk-loader");
    const content = document.getElementById("sdesk-content");
    let clickLock = false;

    const showLoader = () => loader?.classList.remove("sdesk-hidden");
    const hideLoader = () => loader?.classList.add("sdesk-hidden");

    document.querySelectorAll(".sdesk-navbtn").forEach(b => {
        b.addEventListener("click", () => selectButton(b));
    });

    function selectButton(btn) {
        if (clickLock) return;
        clickLock = true; setTimeout(() => clickLock = false, 250);

        document.querySelectorAll(".sdesk-navbtn").forEach(x => x.classList.remove("selected"));
        btn.classList.add("selected");

        const cid = btn.getAttribute("data-content");
        loadContent(cid);
    }

    async function loadContent(contentId) {
        if (!content) return;
        showLoader();

        try {
            const url = `/Content/${contentId}`; // создай Razor-страницы в Pages/Content/*
            const resp = await fetch(url, { cache: "no-store", credentials: "same-origin" });
            const html = resp.ok ? await resp.text() : `<div class="sdesk-card"><h3>Ошибка</h3><p>${resp.status}</p></div>`;

            // Вставляем без класса анимации
            const panel = document.createElement("div");
            panel.className = "sdesk-panel";
            panel.innerHTML = html;
            content.replaceChildren(panel);

            // Старт анимации ОДИН раз
            let started = false;
            const startAnim = () => {
                if (started) return; started = true;
                // форсим reflow
                void panel.offsetWidth;
                panel.classList.add("sdesk-enter");
                panel.addEventListener("animationend", () => {
                    panel.classList.remove("sdesk-enter");
                }, { once: true });
                hideLoader();
            };

            // Если внутри есть iframe — ждём onload (чтобы не было “подмигивания”)
            const iframe = panel.querySelector("iframe");
            if (iframe) {
                const to = setTimeout(startAnim, 1600); // страховка
                iframe.addEventListener("load", () => { clearTimeout(to); startAnim(); }, { once: true });
            } else {
                startAnim();
            }
        } catch (e) {
            console.error(e);
            content.innerHTML = `<div class="sdesk-panel sdesk-enter"><div class="sdesk-card"><h3>Ошибка загрузки</h3></div></div>`;
            hideLoader();
        }
    }

    // при первом заходе — мягко переиграть анимацию стартовой панели, без “мигания”
    (() => {
        const panel = content?.querySelector(".sdesk-panel");
        if (!panel) return;
        // небольшой таймаут, чтобы стиль успел примениться
        requestAnimationFrame(() => {
            void panel.offsetWidth;
            panel.classList.add("sdesk-enter");
            panel.addEventListener("animationend", () => panel.classList.remove("sdesk-enter"), { once: true });
        });
    })();
})();
