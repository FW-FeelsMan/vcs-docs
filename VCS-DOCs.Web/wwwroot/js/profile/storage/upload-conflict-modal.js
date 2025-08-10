(function () {
    function $(sel, root) { return (root || document).querySelector(sel); }

    function ensureIsoWithZone(s) {
        const raw = String(s || '');
        return /Z$|[+\-]\d{2}:?\d{2}$/.test(raw) ? raw : (raw + 'Z');
    }
    function fmtMsk(dt) {
        const d = new Date(ensureIsoWithZone(dt));
        return d.toLocaleString('ru-RU', { timeZone: 'Europe/Moscow', hour12: false });
    }

    function showConflictModal(fileName, conflictType, handlers) {
        var modal = $("#upload-version-modal");
        if (!modal) { console.error("upload-version-modal not found"); return; }

        var title = $("#version-modal-title", modal);
        var message = $("#version-conflict-message", modal);
        var cancelBtn = $("#conflict-cancel", modal);
        var newVersionBtn = $("#conflict-new-version", modal);

        var replaceContainer = $("#split-button", modal);
        var selectedVersionSpan = $("#selected-version", modal);
        var dropdownArrow = $("#version-dropdown", modal);
        var versionList = $("#version-list", modal);

        if (!title || !message || !cancelBtn || !newVersionBtn || !replaceContainer || !selectedVersionSpan || !dropdownArrow || !versionList) {
            console.error("modal parts missing");
            return;
        }

        title.textContent = "Конфликт версий";
        message.textContent = 'Файл "' + fileName + '" уже существует. Выберите действие.';

        var selectedVersion = null;
        selectedVersionSpan.textContent = "V?";
        versionList.innerHTML = "";
        versionList.style.display = "none";

        fetch('/api/Upload/versions?fileName=' + encodeURIComponent(fileName))
            .then(function (r) { return r.ok ? r.json() : []; })
            .then(function (versions) {
                if (Array.isArray(versions) && versions.length) {
                    selectedVersion = versions[0].version ?? versions[0].Version;
                    selectedVersionSpan.textContent = "V" + selectedVersion;
                    versions.forEach(function (ver) {
                        var v = ver.version ?? ver.Version;
                        var dt = ver.uploadedAt ?? ver.UploadedAt;
                        var item = document.createElement("div");
                        item.className = "dropdown-item";
                        item.textContent = "V" + v + " (" + fmtMsk(dt) + ")";
                        item.onclick = function () {
                            selectedVersion = v;
                            selectedVersionSpan.textContent = "V" + v;
                            versionList.style.display = "none";
                        };
                        versionList.appendChild(item);
                    });
                } else {
                    selectedVersionSpan.textContent = "Нет версий";
                }
            })
            .catch(function (e) {
                console.error("versions load error", e);
                selectedVersionSpan.textContent = "Ошибка";
            });

        function toggleList(e) {
            var vis = versionList.style.display === "block";
            versionList.style.display = vis ? "none" : "block";
            if (e) e.stopPropagation();
        }
        dropdownArrow.onclick = toggleList;
        selectedVersionSpan.onclick = toggleList;
        document.addEventListener("click", function (e) {
            if (!replaceContainer.contains(e.target)) versionList.style.display = "none";
        });

        replaceContainer.onclick = function (e) {
            if (e.target === dropdownArrow || e.target === selectedVersionSpan) return;
            if (selectedVersion == null) { alert("Выберите версию для замены"); return; }
            modal.style.display = "none";
            if (handlers && typeof handlers.onReplace === "function") handlers.onReplace(selectedVersion);
        };
        newVersionBtn.onclick = function () {
            modal.style.display = "none";
            if (handlers && typeof handlers.onNewVersion === "function") handlers.onNewVersion();
        };
        cancelBtn.onclick = function () {
            modal.style.display = "none";
            if (handlers && typeof handlers.onCancel === "function") handlers.onCancel();
        };

        modal.style.display = "block";
    }

    window.showConflictModal = showConflictModal;
})();
// ===== SHARE LINK MODAL =====
(function () {
    var $doc = $(document);
    var $modal = $('#share-link-modal');
    var ctx = { gid: null, version: null, filename: null };

    function openMenu($wrap) {
        closeAllMenus();
        $wrap.find('.action-dropdown-menu').show();
    }
    function closeAllMenus() {
        $modal.find('.action-dropdown-menu').hide();
    }
    function setSplitButton($wrap, text, value) {
        var $btn = $wrap.find('> .button-sliding').first();
        $btn.text(text).attr('data-value', value);
    }
    function getSplitValue($wrap) {
        return $wrap.find('> .button-sliding').first().attr('data-value');
    }

    // Меню TTL
    $modal.on('click', '#share-ttl .dropdown-toggle', function () {
        openMenu($('#share-ttl'));
    });
    $modal.on('click', '#share-ttl .dropdown-item', function () {
        var v = $(this).attr('data-value');
        var txt = $(this).text();
        setSplitButton($('#share-ttl'), txt, v);
        closeAllMenus();
    });

    // Меню лимита
    $modal.on('click', '#share-limit .dropdown-toggle', function () {
        openMenu($('#share-limit'));
    });
    $modal.on('click', '#share-limit .dropdown-item', function () {
        var v = $(this).attr('data-value');
        var txt = $(this).text();
        setSplitButton($('#share-limit'), txt, v);
        closeAllMenus();
    });

    // Клик снаружи — закрыть выпадашки
    $(document).on('click', function (e) {
        if ($modal.is(':visible') && !$(e.target).closest('#share-link-modal .split-button').length) {
            closeAllMenus();
        }
    });

    // Открытие модалки из меню действий
    // ожидаем элемент с data-action="share" внутри строки файла
    $doc.off('click.shareOpen').on('click.shareOpen', '[data-action="share"]', function (e) {
        e.preventDefault();

        var $row = $(this).closest('tr');
        var gid = $(this).data('gid') || $row.data('gid') || $row.attr('data-gid');
        var fileName = $(this).data('filename') || $row.find('[data-filename]').data('filename') || $row.find('.file-name').text();
        var verSel = $row.find('select.version-select, select[data-role="version"]').val();
        var version = $(this).data('version') || (verSel ? parseInt(String(verSel).replace(/[^\d]/g, ''), 10) : 1);

        if (!gid || !version) {
            alert('Не удалось определить файл/версию для публикации.');
            return;
        }

        ctx = { gid: gid, version: version, filename: fileName };

        // дефолтные значения
        setSplitButton($('#share-ttl'), '7 дней (168 ч)', '168');
        setSplitButton($('#share-limit'), '10 скачиваний', '10');
        $('#share-auth-only').prop('checked', true);
        $('#share-link-url').hide().val('');

        $modal.show();
    });

    // Закрытие
    $modal.on('click', '#share-cancel', function () {
        $modal.hide();
    });

    // Генерация + копирование
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
                if (res.status === 404) { alert('Файл/версия не найдены.'); }
                else if (res.status === 401) { alert('Требуется вход в систему.'); }
                else { alert('Не удалось создать ссылку. Код: ' + res.status); }
                btn.prop('disabled', false);
                return;
            }

            var data = await res.json();
            var url = data && data.url ? data.url : '';

            if (!url) {
                alert('Сервис вернул пустую ссылку.');
                btn.prop('disabled', false);
                return;
            }

            var $out = $('#share-link-url').val(url).show();
            $out[0].focus(); $out[0].select();
            try { document.execCommand('copy'); } catch { /* ignore */ }

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
