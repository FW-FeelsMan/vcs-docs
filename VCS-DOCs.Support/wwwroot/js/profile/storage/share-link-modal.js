//share-link-modal.js
(function () {
    var $doc = $(document);
    var $modal = $('#share-link-modal');
    var ctx = { gid: null, version: null, filename: null };

    function closeAllMenus() {
        $modal.find('.action-dropdown-menu').hide();
    }
    function openMenu($wrap) {
        closeAllMenus();
        $wrap.find('.action-dropdown-menu').show();
    }
    function setSplitValue($wrap, text, value) {
        $wrap.find('.value').text(text).attr('data-value', value);
    }
    function getSplitValue($wrap) {
        return $wrap.find('.value').attr('data-value');
    }

    $modal.on('click', '#share-ttl .dropdown, #share-ttl .value', function (e) {
        openMenu($('#share-ttl'));
        e.stopPropagation();
    });
    $modal.on('click', '#share-limit .dropdown, #share-limit .value', function (e) {
        openMenu($('#share-limit'));
        e.stopPropagation();
    });

    $modal.on('click', '#share-ttl .action-dropdown-menu .dropdown-item', function () {
        var v = $(this).attr('data-value');
        var txt = $(this).text();
        setSplitValue($('#share-ttl'), txt, v);
        closeAllMenus();
    });
    $modal.on('click', '#share-limit .action-dropdown-menu .dropdown-item', function () {
        var v = $(this).attr('data-value');
        var txt = $(this).text();
        setSplitValue($('#share-limit'), txt, v);
        closeAllMenus();
    });

    $(document).on('click', function (e) {
        if ($modal.is(':visible') && !$(e.target).closest('#share-link-modal .split-button').length) {
            closeAllMenus();
        }
    });

    $doc.off('click.shareOpen').on('click.shareOpen', '[data-action="share"]', function (e) {
        if ($(this).closest('#share-link-modal').length) return;
        e.preventDefault();

        var $row = $(this).closest('tr');
        var gid = $row.data('fileGroupId') || $row.attr('data-file-group-id');
        var version = parseInt(($row.find('.version-button').attr('data-version') || $row.attr('data-current-version') || '1'), 10);
        var fileName = $row.data('fileName') || $row.attr('data-file-name') || ($row.find('td:first .cell-content').text() || '').trim();

        if (!gid || !version) { alert('Не удалось определить файл/версию для публикации.'); return; }

        ctx = { gid: gid, version: version, filename: fileName };

        setSplitValue($('#share-ttl'), '7 дней (168 ч)', '168');
        setSplitValue($('#share-limit'), '10 скачиваний', '10');
        $('#share-auth-only').prop('checked', true);
        $('#share-link-url').hide().val('');

        $modal.show();
    });

    window.openShareLinkModalFromRow = function (rowEl) {
        var $row = $(rowEl);
        var gid = $row.data('fileGroupId') || $row.attr('data-file-group-id');
        var version = parseInt(($row.find('.version-button').attr('data-version') || $row.attr('data-current-version') || '1'), 10);
        var fileName = $row.data('fileName') || $row.attr('data-file-name') || ($row.find('td:first .cell-content').text() || '').trim();

        if (!gid || !version) { alert('Не удалось определить файл/версию для публикации.'); return; }

        ctx = { gid: gid, version: version, filename: fileName };

        setSplitValue($('#share-ttl'), '7 дней (168 ч)', '168');
        setSplitValue($('#share-limit'), '10 скачиваний', '10');
        $('#share-auth-only').prop('checked', true);
        $('#share-link-url').hide().val('');

        $modal.show();
    };

    $modal.on('click', '#share-cancel', function () {
        $modal.hide();
    });

    $modal.on('click', '#share-generate-copy', async function () {
        try {
            var ttl = parseInt(getSplitValue($('#share-ttl')), 10);
            var limitVal = getSplitValue($('#share-limit'));
            var requireAuth = $('#share-auth-only').is(':checked');
            if (!ttl || ttl <= 0) ttl = 168;

            var fd = new FormData();
            fd.append('fileGroupId', ctx.gid);
            fd.append('version', ctx.version);
            fd.append('ttlHours', ttl);
            if (String(limitVal) !== 'unlimited') fd.append('maxDownloads', limitVal);
            fd.append('requireAuth', requireAuth ? 'true' : 'false');

            var btn = $(this);
            btn.prop('disabled', true);

            var res = await fetch('/api/Upload/share-db', { method: 'POST', body: fd });
            if (!res.ok) {
                if (res.status === 404) alert('Файл/версия не найдены.');
                else if (res.status === 401) alert('Требуется вход в систему.');
                else alert('Не удалось создать ссылку. Код: ' + res.status);
                btn.prop('disabled', false);
                return;
            }

            var data = await res.json();
            var url = (data && data.url) ? data.url : '';
            if (!url) { alert('Сервис вернул пустую ссылку.'); btn.prop('disabled', false); return; }

            var $out = $('#share-link-url').val(url).show();
            $out[0].focus(); $out[0].select();
            try { document.execCommand('copy'); } catch { }

            btn.text('Ссылка скопирована!');
            setTimeout(function () {
                btn.text('Скопировать ссылку');
                btn.prop('disabled', false);
                $modal.hide();
            }, 800);
        } catch (err) {
            console.error(err);
            alert('Ошибка при создании ссылки.');
            $('#share-generate-copy').prop('disabled', false);
        }
    });
})();
