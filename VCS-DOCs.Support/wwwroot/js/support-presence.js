// wwwroot/js/support-presence.js
(function () {
    // уже инициализировали? не дублируем
    if (window.__presence?.initialized) {
        console.debug("[presence] already initialized");
        return;
    }

    const statusEl = () => document.querySelector(".main-user-status");
    const setStatus = (cls, text) => {
        const el = statusEl();
        if (!el) return;
        el.classList.remove("online", "offline", "connecting");
        if (cls) el.classList.add(cls);
        if (text) el.textContent = text;
    };

    // Собираем/храним singleton в window.__presence
    const presence = (window.__presence = window.__presence || {});
    presence.initialized = true;

    const hubUrl =
        (window.__presenceHubUrl /* на всякий */) ||
        (location.origin.replace(/\/+$/, "") + "/hubs/userStatus");

    // Создаём соединение один раз
    if (!presence.connection) {
        presence.connection = new signalR.HubConnectionBuilder()
            .withUrl(hubUrl, {
                // Позволяем откатываться на LongPolling, если WebSocket занят/недоступен
                transport:
                    signalR.HttpTransportType.WebSockets |
                    signalR.HttpTransportType.ServerSentEvents |
                    signalR.HttpTransportType.LongPolling,
                // negotiation включён (по умолчанию), не форсим skipNegotiation
            })
            .withAutomaticReconnect({
                // мягкая лесенка ретраев
                nextRetryDelayInMilliseconds: (ctx) => {
                    const steps = [0, 2000, 5000, 10000, 15000, 30000];
                    return steps[Math.min(ctx.previousRetryCount + 1, steps.length - 1)];
                },
            })
            .configureLogging(signalR.LogLevel.Information)
            .build();

        // UI-индикаторы
        presence.connection.onreconnecting((err) => {
            console.warn("[presence] reconnecting:", err?.message || err);
            setStatus("connecting", "Переподключение…");
        });

        presence.connection.onreconnected((id) => {
            console.info("[presence] reconnected:", id);
            setStatus("online", "В сети");
        });

        presence.connection.onclose((err) => {
            console.warn("[presence] closed:", err?.message || err);
            setStatus("offline", "Оффлайн");
            // Автопереподключение из withAutomaticReconnect само попробует,
            // но если оно сдастся, попробуем мягко перезапустить через паузу.
            if (!presence._manualStop) {
                setTimeout(startSafe, 5000);
            }
        });
    }

    // защищённый старт с дедупликацией попыток
    async function startSafe() {
        if (presence._starting || presence._started) return presence._startPromise;
        presence._starting = true;
        setStatus("connecting", "Подключение…");

        presence._startPromise = (async () => {
            let attempt = 0;
            while (!presence._started) {
                try {
                    await presence.connection.start();
                    presence._started = true;
                    console.info("[presence] connected");
                    setStatus("online", "В сети");
                    break;
                } catch (e) {
                    attempt++;
                    console.warn("[presence] start failed:", e?.message || e);
                    // экспонента с верхней границей
                    const delay = Math.min(15000, 800 * Math.pow(2, attempt));
                    setStatus("connecting", "Подключение…");
                    await new Promise((r) => setTimeout(r, delay));
                }
            }
        })();

        return presence._startPromise.finally(() => {
            presence._starting = false;
        });
    }

    // не даём множественным навигациям сломать состояние:
    // не останавливаем соединение на SPA-переключениях, только на реальном unload
    window.addEventListener("beforeunload", () => {
        try {
            presence._manualStop = true;
            if (presence.connection?.state !== "Disconnected") {
                presence.connection.stop();
            }
        } catch { /* noop */ }
    });

    // первый запуск
    console.log("[presence] init on page:", document.title || location.pathname);
    startSafe();
})();
