/* upload-gate.js (dock always on, modes based on REAL server progress) */
(function () {


    if (window.__uploadGateInitialized) return;
    window.__uploadGateInitialized = true;

    var isAuth = (window.userIsAuthenticated === true) || (window.userIsAuthenticated === "true");
    if (!isAuth) return;

    var endpoints = {
        active: "/api/Upload/active",
        restart: "/api/Upload/restart"
    };

    var POLL_MS = 1500;
    var UI_STALE_SECONDS = 30;

    // thresholds
    var PAUSE_STALL_TICKS = 3;        // сколько тиков без роста прогресса => "пауза"
    var FINISH_STALL_TICKS = 4;       // сколько тиков без роста прогресса при 90%+ => "завершение"
    var FINISHING_AT_PERCENT = 99;    // верхняя граница прогресса (мы держим 0..99)

    var state = {
        timer: null,
        lastActive: null,

        // per hash
        lastProgressByHash: {},   // uploadedBytes or uploadedCount (monotonic expectation)
        stallTicksByHash: {},     // ticks without growth of progress
        maxPercentByHash: {}      // anti-rollback for UI
    };
    // ===== Debug switch =====
    var DBG = (window.__UPLOAD_DEBUG === true);
    function nowTime() {
        try { return new Date().toLocaleTimeString(); } catch { return ""; }
    }
    function dbgLog() {
        if (!DBG) return;
        try { console.log.apply(console, arguments); } catch { }
    }
    function dbgWarn() {
        if (!DBG) return;
        try { console.warn.apply(console, arguments); } catch { }
    }

    function safeHash(active) {
        return (active && active.fileHash) ? String(active.fileHash) : "";
    }

    function hasFreshInfo(active) {
        return active && (typeof active.isFresh === "boolean" || typeof active.ageSec === "number");
    }

    function isStale(active) {
        if (!active) return false;
        if (typeof active.isFresh === "boolean") return active.isFresh === false;
        if (typeof active.ageSec === "number") return active.ageSec > UI_STALE_SECONDS;
        return false;
    }

    function progressValue(active) {
        // IMPORTANT: only for mode logic; prefer uploadedBytes.
        if (!active) return 0;
        if (typeof active.uploadedBytes === "number") return active.uploadedBytes;
        if (Array.isArray(active.uploaded)) return active.uploaded.length;
        return 0;
    }

    function rawPercent(active) {
        if (!active) return 0;

        if (typeof active.uploadedBytes === "number" && active.fileSize > 0) {
            var p = Math.floor((active.uploadedBytes / active.fileSize) * 100);
            if (p < 0) p = 0;
            if (p > 99) p = 99;
            return p;
        }

        if (Array.isArray(active.uploaded) && active.fileSize > 0) {
            var approx = (active.uploaded.length * (16 * 1024 * 1024)) / active.fileSize;
            var pp = Math.floor(approx * 100);
            if (pp < 0) pp = 0;
            if (pp > 99) pp = 99;
            return pp;
        }

        return 0;
    }

    function stablePercent(active) {
        // anti-rollback only for UI (проценты не падают назад)
        var h = safeHash(active);
        var p = rawPercent(active);
        if (!h) return p;

        var maxP = state.maxPercentByHash[h];
        if (typeof maxP !== "number") maxP = 0;

        if (p < maxP) p = maxP;
        if (p > maxP) state.maxPercentByHash[h] = p;
        else state.maxPercentByHash[h] = maxP;

        return p;
    }

    function updateStall(active) {
        var h = safeHash(active);
        if (!h) return { grew: false, ticks: 0 };

        var cur = progressValue(active);
        var prev = state.lastProgressByHash[h];

        if (typeof prev !== "number") prev = cur;

        var grew = cur > prev;

        if (grew) {
            state.stallTicksByHash[h] = 0;
        } else {
            state.stallTicksByHash[h] = (state.stallTicksByHash[h] || 0) + 1;
        }

        state.lastProgressByHash[h] = cur;

        return { grew: grew, ticks: state.stallTicksByHash[h] || 0 };
    }

    function computeMode(active, percent, grew, stallTicks) {
        if (!active) return { mode: null, reason: "no-active" };

        if (active.stopped) return { mode: "paused", reason: "server-stopped=true" };

        if (grew) return { mode: "uploading", reason: "progress-grew" };

        if (percent >= FINISHING_AT_PERCENT) return { mode: "finishing", reason: "percent>=99" };

        if (percent >= 90 && stallTicks >= PAUSE_STALL_TICKS)
            return { mode: "finishing", reason: ">=90% + stall>=pauseThreshold => finishing" };

        if (percent >= 90 && stallTicks >= FINISH_STALL_TICKS)
            return { mode: "finishing", reason: ">=90% + stall>=finishThreshold" };

        if (stallTicks >= PAUSE_STALL_TICKS) {
            if (!hasFreshInfo(active)) return { mode: "paused", reason: "stall>=pauseThreshold + no freshness info" };
            if (isStale(active)) return { mode: "paused", reason: "stall>=pauseThreshold + stale session" };
        }

        return { mode: "uploading", reason: "default" };
    }


    function ensureDock() {
        var root = document.getElementById("upload-gate-dock");
        if (root) return root;

        root = document.createElement("div");
        root.id = "upload-gate-dock";
        root.className = "upload-gate-dock";

        root.innerHTML = `
          <div class="upload-gate__header">
            <div class="upload-gate__title">
              <span class="upload-gate__status">Загрузка</span>
              <span class="upload-gate__percent">0%</span>
            </div>
            <div class="upload-gate__hdr-actions">
              <button type="button" class="upload-gate__iconbtn" id="upload-gate-toggle" title="Свернуть/развернуть">▾</button>
              <button type="button" class="upload-gate__iconbtn" id="upload-gate-close" title="Скрыть">✕</button>
            </div>
          </div>

          <div class="upload-gate__body">
            <div class="upload-gate__filename" title=""></div>

            <div class="upload-gate__bar">
              <div class="upload-gate__barfill" style="width:0%"></div>
            </div>

            <div class="upload-gate__meta">
              <span class="upload-gate__meta-left"></span>
              <span class="upload-gate__meta-right"></span>
            </div>

            <div class="upload-gate__actions">
              <button type="button" class="upload-gate__btn upload-gate__btn--primary" id="upload-gate-continue">Продолжить</button>
              <button type="button" class="upload-gate__btn upload-gate__btn--danger" id="upload-gate-cancel">Отменить</button>
            </div>
          </div>
        `;

        document.body.appendChild(root);

        var toggleBtn = root.querySelector("#upload-gate-toggle");
        var closeBtn = root.querySelector("#upload-gate-close");

        if (toggleBtn) {
            toggleBtn.onclick = function () {
                root.classList.toggle("is-collapsed");
                try { localStorage.setItem("__uploadGateCollapsed", root.classList.contains("is-collapsed") ? "1" : "0"); } catch { }
            };
        }

        if (closeBtn) {
            closeBtn.onclick = function () {
                root.classList.remove("is-open");
            };
        }

        try {
            var v = localStorage.getItem("__uploadGateCollapsed");
            if (v === "1") root.classList.add("is-collapsed");
        } catch { }

        return root;
    }

    function openProfilePage() {
        var btn = document.querySelector('.sidebar-button[data-content="profile_page"]');
        if (btn) { btn.click(); return; }
        if (typeof window.selectButton === "function") {
            var any = document.querySelector(".sidebar-button");
            if (any) any.click();
        }
    }

    async function cancelOnServer(active) {
        try {
            var fd = new FormData();
            fd.append("fileName", (active && active.fileName) ? active.fileName : "");
            fd.append("fileHash", (active && active.fileHash) ? active.fileHash : "");
            await fetch(endpoints.restart, { method: "POST", body: fd, credentials: "same-origin" });
        } catch { }
    }

    function render(active, mode, percent) {
        var root = ensureDock();
        var statusEl = root.querySelector(".upload-gate__status");
        var percentEl = root.querySelector(".upload-gate__percent");
        var fnEl = root.querySelector(".upload-gate__filename");
        var fillEl = root.querySelector(".upload-gate__barfill");
        var leftEl = root.querySelector(".upload-gate__meta-left");
        var rightEl = root.querySelector(".upload-gate__meta-right");
        var btnContinue = root.querySelector("#upload-gate-continue");
        var btnCancel = root.querySelector("#upload-gate-cancel");

        var name = (active && active.fileName) ? active.fileName : "файл";

        root.classList.toggle("mode-uploading", mode === "uploading");
        root.classList.toggle("mode-paused", mode === "paused");
        root.classList.toggle("mode-finishing", mode === "finishing");

        if (statusEl) {
            if (mode === "paused") statusEl.textContent = "Пауза";
            else if (mode === "finishing") statusEl.textContent = "Завершение";
            else statusEl.textContent = "Загрузка";
        }

        if (percentEl) percentEl.textContent = percent + "%";
        if (fnEl) { fnEl.textContent = name; fnEl.title = name; }
        if (fillEl) fillEl.style.width = percent + "%";

        if (leftEl) {
            if (mode === "paused") {
                leftEl.textContent = "Загрузка прервалась. Нажмите «Продолжить» и выберите тот же файл.";
            } else if (mode === "finishing") {
                leftEl.textContent = "Идёт завершение загрузки (сборка/перемещение). Это может занять немного времени.";
            } else {
                leftEl.textContent = "Идёт загрузка. Можно уйти с этой страницы — окно останется.";
            }
        }

        if (rightEl) rightEl.textContent = "";

        if (btnContinue) {
            btnContinue.style.display = (mode === "paused") ? "inline-flex" : "none";
            btnContinue.onclick = function () {
                try {
                    window.__uploadGateResumeIntent = {
                        fileHash: active ? (active.fileHash || null) : null,
                        fileName: active ? (active.fileName || null) : null,
                        fileSize: active ? (active.fileSize || null) : null
                    };
                } catch { }

                if (typeof window.__uploadGateOpenPicker === "function") {
                    try {
                        window.__uploadGateOpenPicker(window.__uploadGateResumeIntent);
                        return;
                    } catch { }
                }

                openProfilePage();
            };
        }

        if (btnCancel) {
            btnCancel.style.display = (mode === "paused" || mode === "uploading" || mode === "finishing") ? "inline-flex" : "none";
            btnCancel.onclick = async function () {
                await cancelOnServer(active);
                try { window.__uploadGateResumeIntent = null; } catch { }
            };
        }

        root.classList.add("is-open");
    }

    function hide() {
        var root = document.getElementById("upload-gate-dock");
        if (root) root.classList.remove("is-open");
        state.lastActive = null;
    }

    async function tick() {
        try {
            var r = await fetch(endpoints.active, { cache: "no-store", credentials: "same-origin" });
            if (!r.ok) {
                dbgWarn(`[GATE ${nowTime()}] /active HTTP ${r.status}`);
                return;
            }


            var active = await r.json();
            if (active && active.found) {
                state.lastActive = active;

                var stall = updateStall(active);
                var percent = stablePercent(active);
                var cm = computeMode(active, percent, stall.grew, stall.ticks);
                var mode = cm.mode;

                render(active, mode, percent);

                // ===== Debug output =====
                dbgLog(
                    `[GATE ${nowTime()}] mode=${mode}(${cm.reason}) percent=${percent}% ` +
                    `grew=${stall.grew} stallTicks=${stall.ticks} ` +
                    `ageSec=${active.ageSec} isFresh=${active.isFresh} stopped=${active.stopped} ` +
                    `uploadedBytes=${active.uploadedBytes}/${active.fileSize} ` +
                    `uploadedCount=${Array.isArray(active.uploaded) ? active.uploaded.length : "?"} ` +
                    `hash=${String(active.fileHash || "").slice(0, 18)}...`
                );

            } else {
                hide();
            }
        } catch { }
    }

    function start() {
        if (state.timer) return;
        state.timer = setInterval(tick, POLL_MS);
        tick();
    }

    document.addEventListener("visibilitychange", function () {
        if (!document.hidden) tick();
    });
    window.addEventListener("focus", function () { tick(); });

    start();
})();
