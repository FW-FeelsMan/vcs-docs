// demo-оживление дашборда: случайные данные + плавное обновление
(() => {
    // безопасный селектор
    const $ = (s, r = document) => r.querySelector(s);

    // ---- KPI кольца ---------------------------------------------------------
    function setRing(el, percent, label) {
        const deg = Math.max(0, Math.min(100, percent)) * 3.6;
        el.style.background =
            `conic-gradient(var(--wl-primary) ${deg}deg, transparent ${deg}deg 360deg),
       radial-gradient(circle 26px at 50% 50%, var(--wl-panel) 98%, transparent 100%)`;
        const span = el.querySelector('.ring-val');
        if (span) span.textContent = label ?? `${Math.round(percent)}%`;
    }

    // маленький помощник для сглаженного рандома
    const makeDrift = (start = 50, min = 0, max = 100, step = 6) => {
        let v = start;
        return () => {
            v += (Math.random() * 2 - 1) * step;
            v = Math.max(min, Math.min(max, v));
            return v;
        };
    };

    // создаём генераторы метрик
    const gCpu = makeDrift(42, 8, 96, 5);
    const gRam = makeDrift(61, 10, 98, 3.2);
    const gDisk = makeDrift(28, 2, 92, 8);
    const gNet = makeDrift(35, 0, 100, 6);
    const gRps = makeDrift(120, 20, 350, 18);
    const gErr = makeDrift(1.8, 0, 9, 0.8);

    // ---- списки (сервисы/очереди) ------------------------------------------
    const services = [
        { name: 'Auth/Identity', state: 'ok' },
        { name: 'SQL (SQLite)', state: 'ok' },
        { name: 'Mail (SMTP)', state: 'ok' },
        { name: 'VDocs Bridge', state: 'warn' },
        { name: 'SignalR Hubs', state: 'ok' },
    ];
    const queues = [
        { name: 'MailQueue', size: 2 },
        { name: 'Reports', size: 0 },
        { name: 'Indexing', size: 4 },
        { name: 'Exports', size: 1 },
    ];

    function renderLists() {
        const svcUl = $('#svcList'); const qUl = $('#queueList');
        if (svcUl) {
            svcUl.innerHTML = services.map(s =>
                `<li><span>${s.name}</span><span class="badge ${s.state}">${s.state === 'ok' ? 'OK' : s.state}</span></li>`
            ).join('');
        }
        if (qUl) {
            qUl.innerHTML = queues.map(q =>
                `<li><span>${q.name}</span><span class="badge ${q.size > 5 ? 'bad' : q.size > 0 ? 'warn' : 'ok'}">${q.size}</span></li>`
            ).join('');
        }
    }

    // ---- таблица эндпоинтов -------------------------------------------------
    const demoEndpoints = [
        { path: '/api/support/accounts', avg: 42, p95: 85, rps: 18, err: 0.1 },
        { path: '/api/support/tickets', avg: 65, p95: 120, rps: 12, err: 0.5 },
        { path: '/hubs/userStatus', avg: 15, p95: 28, rps: 30, err: 0.0 },
        { path: '/api/files/search', avg: 120, p95: 260, rps: 6, err: 1.4 },
        { path: '/api/vdocs/preview', avg: 90, p95: 190, rps: 8, err: 0.8 },
    ];
    function renderEndpoints() {
        const tb = $('#tblEndpoints tbody');
        if (!tb) return;
        tb.innerHTML = demoEndpoints.map(e => {
            const cls = e.err > 2 ? 'bad' : e.err > 0.7 ? 'warn' : 'ok';
            return `<tr>
          <td title="${e.path}">${e.path}</td>
          <td>${e.avg.toFixed(0)}</td>
          <td>${e.p95.toFixed(0)}</td>
          <td>${e.rps.toFixed(0)}</td>
          <td><span class="badge ${cls}">${e.err.toFixed(1)}</span></td>
        </tr>`;
        }).join('');
    }

    // ---- Chart.js графики ---------------------------------------------------
    const charts = {};

    function makeLine(ctx, labels, datasets) {
        return new Chart(ctx, {
            type: 'line',
            data: { labels, datasets },
            options: {
                animation: { duration: 300 },
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { labels: { color: '#cbd5e1' } } },
                scales: {
                    x: { ticks: { color: '#9aa3b2' }, grid: { color: 'rgba(255,255,255,.04)' } },
                    y: { ticks: { color: '#9aa3b2' }, grid: { color: 'rgba(255,255,255,.06)' } }
                }
            }
        });
    }

    function initCharts() {
        const labels = Array.from({ length: 30 }, (_, i) => `${i}`);
        const cpu = labels.map(() => gCpu()); const ram = labels.map(() => gRam());
        const rps = labels.map(() => gRps()); const err = labels.map(() => gErr());
        const inb = labels.map(() => gNet()); const out = labels.map(() => gNet());

        charts.cpuRam = makeLine($('#chartCpuRam'), labels, [
            { label: 'CPU %', data: cpu, borderColor: '#60a5fa', backgroundColor: 'rgba(96,165,250,.15)', tension: .3, fill: true },
            { label: 'RAM %', data: ram, borderColor: '#a78bfa', backgroundColor: 'rgba(167,139,250,.12)', tension: .3, fill: true },
        ]);

        charts.rpsErr = makeLine($('#chartRpsErr'), labels, [
            { label: 'RPS', data: rps, borderColor: '#22c55e', backgroundColor: 'rgba(34,197,94,.12)', tension: .3, fill: true, yAxisID: 'y' },
            { label: 'Errors %', data: err, borderColor: '#ef4444', backgroundColor: 'rgba(239,68,68,.12)', tension: .3, fill: true, yAxisID: 'y1' },
        ]);
        charts.rpsErr.options.scales.y1 = { position: 'right', grid: { drawOnChartArea: false }, ticks: { color: '#fca5a5' } };

        charts.net = makeLine($('#chartNet'), labels, [
            { label: 'In', data: inb, borderColor: '#34d399', backgroundColor: 'rgba(52,211,153,.12)', tension: .3, fill: true },
            { label: 'Out', data: out, borderColor: '#5eead4', backgroundColor: 'rgba(94,234,212,.10)', tension: .3, fill: true },
        ]);
    }

    // ---- периодическое обновление ------------------------------------------
    function tick() {
        // KPI
        const cpu = gCpu(), ram = gRam(), disk = gDisk(), net = gNet(), rps = gRps(), err = gErr();
        setRing(document.querySelector('.ring[data-val="cpu"]'), cpu);
        setRing(document.querySelector('.ring[data-val="ram"]'), ram);
        setRing(document.querySelector('.ring[data-val="disk"]'), disk);
        setRing(document.querySelector('.ring[data-val="net"]'), net);
        setRing(document.querySelector('.ring[data-val="rps"]'), Math.min(100, rps / 4), String(Math.round(rps)));
        setRing(document.querySelector('.ring[data-val="err"]'), err * 10, `${err.toFixed(1)}%`);
        $('#kpiCpuInfo').textContent = `сред. ${cpu.toFixed(0)}%`;
        $('#kpiRamInfo').textContent = `${ram.toFixed(0)}% занято`;
        $('#kpiDiskInfo').textContent = `${(disk / 1.2).toFixed(0)} MB/s`;
        $('#kpiNetInfo').textContent = `${(net * 1.3).toFixed(0)} Mbit/s`;
        $('#kpiRpsInfo').textContent = `${rps.toFixed(0)} req/s`;
        $('#kpiErrInfo').textContent = `${err.toFixed(1)}% 4xx/5xx`;

        // графики – сдвигаем окно и добавляем точку
        const push = (chart, v1, v2) => {
            chart.data.labels.push(String(Date.now() % 60000));
            chart.data.labels.shift();
            chart.data.datasets[0].data.push(v1);
            chart.data.datasets[0].data.shift();
            if (v2 != null) { chart.data.datasets[1].data.push(v2); chart.data.datasets[1].data.shift(); }
            chart.update('none');
        };
        push(charts.cpuRam, cpu, ram);
        push(charts.rpsErr, rps, err);
        push(charts.net, gNet(), gNet());

        // «живые» числа в таблице
        demoEndpoints.forEach(e => {
            e.avg = Math.max(10, e.avg + (Math.random() * 2 - 1) * 6);
            e.p95 = Math.max(e.avg, e.p95 + (Math.random() * 2 - 1) * 10);
            e.rps = Math.max(1, e.rps + (Math.random() * 2 - 1) * 3);
            e.err = Math.max(0, e.err + (Math.random() * 2 - 1) * 0.2);
        });
        renderEndpoints();
    }

    // --- автопереключение интервала ----------------------------------------
    let timer = null;
    function startAuto() { stopAuto(); timer = setInterval(tick, 2000); }
    function stopAuto() { if (timer) { clearInterval(timer); timer = null; } }

    // init
    document.addEventListener('DOMContentLoaded', () => {
        renderLists();
        renderEndpoints();
        initCharts();
        startAuto();

        const btn = $('#wl-refresh');
        btn?.addEventListener('click', () => {
            const on = btn.dataset.state !== 'off';
            if (on) { stopAuto(); btn.dataset.state = 'off'; btn.textContent = 'Ручной режим'; }
            else { startAuto(); btn.dataset.state = 'auto'; btn.textContent = 'Авто-обновление'; }
        });

        $('#wl-range')?.addEventListener('change', e => {
            // тут можно переключать сглаживание/кол-во точек/диапазон при подключении к реальному API
            // сейчас просто перерисуем без изменений
            Object.values(charts).forEach(ch => ch.update());
        });
    });
})();
