// The Kids statistics page (feature 22): pick a kid and a period, then see the time per day (stacked by series, with
// the daily allowance as a dashed line), the most watched series with their episodes, when the kid watches (a heat
// map by hour, weekdays and weekends apart) and on which device. Minutes per day come from the daily screen time and
// always show; the rest comes from the watch log, kept only with "Advanced statistics" on. The settings card turns
// it on or off, sets how long data is kept and deletes the watch log.
// Data comes only from authenticated calls; the page HTML is served anonymously and holds none. Every name is
// inserted as text, and the charts are SVG built with DOM calls. English or Spanish, from the viewer's language.

const texts = {
    en: {
        title: 'Kids statistics',
        intro: 'How much each kid watches, what and when. Everything is counted on this server, from every app.',
        kid: 'Kid',
        range: 'Period',
        lastDays: 'Last {0} days',
        noKids: 'No kids yet. Mark kids on the Kids page first.',
        offTitle: 'Advanced statistics are off.',
        offBody: 'Minutes per day still show, from the daily screen time. Turn on advanced statistics below to see what each kid watches, when and on which device.',
        offKept: 'Below is what was kept before it was turned off; it is deleted as it ages out.',
        tabDay: 'By day',
        tabSeries: 'By series',
        tabHour: 'By hour',
        tabDevice: 'By device',
        tabsLabel: 'Statistics views',
        dayTitle: 'Time per day',
        dayBySeries: 'grouped by series',
        seriesTitle: 'Most watched',
        seriesHint: 'open a series to see its episodes',
        hourTitle: 'When they watch',
        hourHint: 'server time',
        deviceTitle: 'Where they watch',
        settingsTitle: 'Statistics settings',
        advanced: 'Advanced statistics',
        advancedHelp: 'Keeps what each kid watches, when and on which device. Off by default.',
        advancedOff: 'Turning it off stops recording at once. What was already kept stays until it is older than the days below, or until you delete it here.',
        retention: 'Keep data for',
        retentionHelp: 'Applies to minutes per day and to the watch log. Older days are deleted every night by the "Prune kids screen time" task; a shorter period applies from its next run.',
        days: '{0} days',
        privacy: 'Everything stays on this server. Nothing is sent anywhere: no telemetry, no third parties.',
        save: 'Save',
        delete: 'Delete the watch log',
        deleteTitle: 'Delete the watch log',
        deleteText: 'Delete what every kid watched, when and where? Minutes per day stay. This cannot be undone.',
        deleted: 'Watch log deleted',
        saved: 'Statistics settings saved',
        saveFailed: 'Could not save the statistics settings.',
        deleteFailed: 'Could not delete the watch log.',
        loadFailed: 'Could not load the statistics. Check the server log.',
        held: 'Watch log: {0} stretches and {1} breaks kept, since {2}.',
        heldEmpty: 'The watch log is empty.',
        total: 'watched',
        average: 'a day on average',
        overLimit: 'days over the limit',
        breaks: 'breaks',
        series: 'Series',
        plays: 'Plays',
        time: 'Time',
        last: 'Last',
        device: 'Device',
        app: 'App',
        others: 'Others',
        notInLog: 'Not in the log',
        limit: 'Daily allowance',
        live: 'live',
        today: 'today',
        yesterday: 'yesterday',
        weekdays: 'Mon–Fri',
        weekend: 'Sat–Sun',
        nothing: 'Nothing watched in this period.',
        needsAdvanced: 'Turn on advanced statistics to see this.',
        unknown: 'Unknown',
        min: '{0} min',
        hours: '{0} h {1}',
        season: 'S{0} · E{1} · {2}',
        expand: 'Show episodes of {0}',
        breakSummary: '{0} breaks, {1}'
    },
    es: {
        title: 'Estadísticas',
        intro: 'Cuánto ve cada hijo, qué y cuándo. Todo se cuenta en este servidor, desde cualquier app.',
        kid: 'Hijo',
        range: 'Periodo',
        lastDays: 'Últimos {0} días',
        noKids: 'Todavía no hay niños. Márcalos primero en la página Kids.',
        offTitle: 'Las estadísticas avanzadas están apagadas.',
        offBody: 'Los minutos por día se siguen viendo, del tiempo al día. Enciende las estadísticas avanzadas abajo para ver qué ve cada hijo, cuándo y en qué dispositivo.',
        offKept: 'Debajo está lo que se guardó antes de apagarlas; se borra al caducar.',
        tabDay: 'Por día',
        tabSeries: 'Por serie',
        tabHour: 'Por horas',
        tabDevice: 'Por dispositivo',
        tabsLabel: 'Vistas de estadísticas',
        dayTitle: 'Tiempo por día',
        dayBySeries: 'agrupado por serie',
        seriesTitle: 'Lo más visto',
        seriesHint: 'pulsa una serie para ver sus episodios',
        hourTitle: 'A qué horas ve',
        hourHint: 'hora del servidor',
        deviceTitle: 'Dónde ve',
        settingsTitle: 'Ajustes de estadísticas',
        advanced: 'Estadísticas avanzadas',
        advancedHelp: 'Guarda qué ve cada hijo, cuándo y en qué dispositivo. Apagado de serie.',
        advancedOff: 'Al apagarlo se deja de registrar en el acto. Lo ya guardado se queda hasta que sea más antiguo que los días de abajo, o hasta que lo borres aquí.',
        retention: 'Guardar datos durante',
        retentionHelp: 'Vale para los minutos por día y para el registro de visionado. Lo más antiguo se borra cada noche con la tarea "Prune kids screen time"; un periodo más corto se aplica desde su siguiente ejecución.',
        days: '{0} días',
        privacy: 'Todo se queda en este servidor. No se manda nada fuera: ni telemetría ni terceros.',
        save: 'Guardar',
        delete: 'Borrar el registro de visionado',
        deleteTitle: 'Borrar el registro de visionado',
        deleteText: '¿Borrar qué vio cada hijo, cuándo y dónde? Los minutos por día se quedan. No se puede deshacer.',
        deleted: 'Registro de visionado borrado',
        saved: 'Ajustes de estadísticas guardados',
        saveFailed: 'No se pudieron guardar los ajustes de estadísticas.',
        deleteFailed: 'No se pudo borrar el registro de visionado.',
        loadFailed: 'No se pudieron cargar las estadísticas. Mira el registro del servidor.',
        held: 'Registro: {0} tramos y {1} descansos guardados, desde el {2}.',
        heldEmpty: 'El registro de visionado está vacío.',
        total: 'visto',
        average: 'al día de media',
        overLimit: 'días por encima del límite',
        breaks: 'descansos',
        series: 'Serie',
        plays: 'Veces',
        time: 'Tiempo',
        last: 'Último',
        device: 'Dispositivo',
        app: 'App',
        others: 'Otros',
        notInLog: 'Fuera del registro',
        limit: 'Tiempo al día',
        live: 'en directo',
        today: 'hoy',
        yesterday: 'ayer',
        weekdays: 'L–V',
        weekend: 'S–D',
        nothing: 'No vio nada en este periodo.',
        needsAdvanced: 'Enciende las estadísticas avanzadas para ver esto.',
        unknown: 'Desconocido',
        min: '{0} min',
        hours: '{0} h {1}',
        season: 'T{0} · E{1} · {2}',
        expand: 'Ver episodios de {0}',
        breakSummary: '{0} descansos, {1}'
    }
};

const language = String(document.documentElement.lang || navigator.language || 'en').toLowerCase().startsWith('es') ? 'es' : 'en';

function t(key, ...args) {
    const text = texts[language][key] || texts.en[key] || key;
    return text.replace(/\{(\d)\}/g, (_, i) => String(args[Number(i)]));
}

// Five series colours of different lightness (so they read apart in both themes and for colour-blind viewers), then
// grey for the rest and a pale grey for time the log does not have (other days' offline reports, or before it was on)
const seriesColours = ['#1565c0', '#ef8a17', '#2e7d32', '#9c4dcc', '#00a5b5'];
const othersColour = '#8a8a8a';
const outsideColour = '#c9ced3';
const svgNs = 'http://www.w3.org/2000/svg';

function request(type, path, body) {
    const options = { type, url: ApiClient.getUrl(path) };
    if (type !== 'DELETE') {
        options.dataType = 'json';
    }

    if (body !== undefined) {
        options.data = JSON.stringify(body);
        options.contentType = 'application/json';
    }

    return ApiClient.ajax(options);
}

function element(tag, className, text) {
    const node = document.createElement(tag);
    if (className) {
        node.className = className;
    }

    if (text !== undefined) {
        node.textContent = text;
    }

    return node;
}

function svg(tag, attributes, text) {
    const node = document.createElementNS(svgNs, tag);
    for (const [name, value] of Object.entries(attributes || {})) {
        node.setAttribute(name, String(value));
    }

    if (text !== undefined) {
        node.textContent = text;
    }

    return node;
}

function escapeHtml(text) {
    const escapes = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', '\'': '&#39;' };
    return String(text).replace(/[&<>"']/g, (c) => escapes[c]);
}

function setText(node, text) {
    node.textContent = text || '';
    node.hidden = !text;
}

// 42 min, 2 h 05
function duration(seconds) {
    const minutes = Math.round((seconds || 0) / 60);
    if (minutes < 60) {
        return t('min', minutes);
    }

    return t('hours', Math.floor(minutes / 60), String(minutes % 60).padStart(2, '0'));
}

function parseDay(day) {
    const [y, m, d] = day.split('-').map(Number);
    return new Date(y, m - 1, d);
}

function dayLabel(day, style) {
    return parseDay(day).toLocaleDateString(language, style || { day: 'numeric', month: 'short' });
}

// "today 18:05", "yesterday", a weekday within the week, else the date
function lastSeen(utc, today) {
    const when = new Date(utc);
    const local = new Date(when.getFullYear(), when.getMonth(), when.getDate());
    const days = Math.round((parseDay(today) - local) / 86_400_000);
    if (days <= 0) {
        return t('today') + ' ' + when.toLocaleTimeString(language, { hour: '2-digit', minute: '2-digit' });
    }

    if (days === 1) {
        return t('yesterday');
    }

    return days < 7 ? when.toLocaleDateString(language, { weekday: 'long' }) : when.toLocaleDateString(language, { day: 'numeric', month: 'short' });
}

// The plugin id is looked up by name, so this anonymously served file holds no id
let pluginIdPromise = null;

function pluginId() {
    if (!pluginIdPromise) {
        pluginIdPromise = ApiClient.getJSON(ApiClient.getUrl('Plugins'))
            .then((plugins) => {
                const plugin = plugins.find((candidate) => candidate.Name === 'Kids');
                if (!plugin) {
                    throw new Error('The Kids plugin is not listed');
                }

                return plugin.Id;
            })
            .catch((error) => {
                pluginIdPromise = null;
                throw error;
            });
    }

    return pluginIdPromise;
}

// Reads the configuration, changes it and writes it all back, so the Kids page's settings are kept
function updateConfiguration(change) {
    return pluginId().then((id) => ApiClient.getPluginConfiguration(id).then((config) => {
        change(config);
        return ApiClient.updatePluginConfiguration(id, config);
    }));
}

const retentionChoices = [30, 90, 180, 365];
const rangeChoices = [7, 14, 30, 90, 180, 365];

// Page state
const state = { kids: [], retention: 90, advanced: false, tab: 'day', stats: null, open: new Set() };

function showError(view, message) {
    setText(view.querySelector('.kidsError'), message);
}

function translatePage(view) {
    view.querySelectorAll('[data-i18n]').forEach((node) => {
        node.textContent = t(node.dataset.i18n);
    });
    view.querySelector('.kidsTabs').setAttribute('aria-label', t('tabsLabel'));
}

function fillRanges(view) {
    const select = view.querySelector('.kidsStatsRange');
    const current = Number(select.value) || 14;
    const choices = rangeChoices.filter((days) => days <= state.retention);
    select.replaceChildren(...choices.map((days) => {
        const option = element('option', null, t('lastDays', days));
        option.value = String(days);
        return option;
    }));
    select.value = String(choices.includes(current) ? current : choices.filter((d) => d <= current).pop() || choices[0]);
}

function fillRetention(view) {
    const select = view.querySelector('.kidsRetention');
    select.replaceChildren(...retentionChoices.map((days) => {
        const option = element('option', null, t('days', days));
        option.value = String(days);
        return option;
    }));
    select.value = String(state.retention);
}

// Summary tiles: total, average, days over the limit, breaks
function renderSummary(view, stats) {
    const used = stats.Days.reduce((sum, day) => sum + day.UsedSeconds, 0);
    const over = stats.Days.filter((day) => day.AllowanceMinutes !== null && day.AllowanceMinutes !== undefined
        && day.UsedSeconds > (day.AllowanceMinutes + day.BonusMinutes) * 60).length;
    const tiles = [[duration(used), t('total')], [duration(used / stats.Days.length), t('average')], [String(over), t('overLimit')]];
    if (stats.AdvancedStats || stats.Breaks.length > 0) {
        const count = stats.Breaks.reduce((sum, day) => sum + day.Count, 0);
        tiles.push([String(count), t('breaks')]);
    }

    view.querySelector('.kidsSummary').replaceChildren(...tiles.map(([value, label]) => {
        const tile = element('div');
        tile.append(element('b', null, value), element('span', null, label));
        return tile;
    }));
}

// The five most watched over the period get a colour each, the rest are "Others"
function colourKeys(stats) {
    const keys = new Map();
    stats.Series.slice(0, seriesColours.length).forEach((series, i) => keys.set(series.Key, { colour: seriesColours[i], name: series.Name }));
    return keys;
}

function renderDays(view, stats) {
    const chart = view.querySelector('.kidsDayChart');
    const legend = view.querySelector('.kidsDayLegend');
    const hasLog = stats.Series.length > 0;
    view.querySelector('.kidsDaySubtitle').textContent = hasLog ? t('dayBySeries') : '';
    const keys = colourKeys(stats);
    const width = 720;
    const height = 240;
    const left = 44;
    const bottom = 26;
    const top = 10;
    const plotHeight = height - bottom - top;
    const maxMinutes = Math.max(30, ...stats.Days.map((d) => d.UsedSeconds / 60),
        ...stats.Days.map((d) => (d.AllowanceMinutes === null || d.AllowanceMinutes === undefined ? 0 : d.AllowanceMinutes + d.BonusMinutes)));
    const step = maxMinutes > 240 ? 120 : maxMinutes > 120 ? 60 : 30;
    const scaleMax = Math.ceil(maxMinutes / step) * step;
    const y = (minutes) => top + plotHeight - (minutes / scaleMax) * plotHeight;
    const slot = (width - left) / stats.Days.length;
    const barWidth = Math.max(2, Math.min(36, slot * 0.7));
    const root = svg('svg', { viewBox: '0 0 ' + width + ' ' + height, role: 'img', 'aria-label': t('dayTitle') });
    for (let minutes = 0; minutes <= scaleMax; minutes += step) {
        root.append(svg('line', { class: 'kidsGridLine', x1: left, x2: width, y1: y(minutes), y2: y(minutes) }));
        root.append(svg('text', { x: left - 6, y: y(minutes) + 4, 'text-anchor': 'end' }, minutes >= 60 ? (minutes / 60) + ' h' : minutes + ''));
    }

    const labelEvery = Math.ceil(stats.Days.length / 14);
    let usedOutside = false;
    let usedOthers = false;
    stats.Days.forEach((day, i) => {
        const x = left + (i * slot) + ((slot - barWidth) / 2);
        const group = svg('g');
        group.append(svg('title', {}, dayLabel(day.Day, { weekday: 'short', day: 'numeric', month: 'short' }) + ': ' + duration(day.UsedSeconds)));
        let base = 0;
        const parts = [];
        let others = 0;
        for (const part of day.Series) {
            if (keys.has(part.Key)) {
                parts.push({ seconds: part.Seconds, colour: keys.get(part.Key).colour });
            } else {
                others += part.Seconds;
            }
        }

        if (others > 0) {
            usedOthers = true;
            parts.push({ seconds: others, colour: othersColour });
        }

        const logged = day.Series.reduce((sum, part) => sum + part.Seconds, 0);
        const outside = day.UsedSeconds - logged;
        if (outside > 30) {
            usedOutside = usedOutside || hasLog;
            parts.push({ seconds: outside, colour: hasLog ? outsideColour : seriesColours[0] });
        }

        for (const part of parts) {
            const minutes = part.seconds / 60;
            const rectTop = y(base + minutes);
            group.append(svg('rect', { x, y: rectTop, width: barWidth, height: Math.max(0, y(base) - rectTop), fill: part.colour }));
            base += minutes;
        }

        if (day.AllowanceMinutes !== null && day.AllowanceMinutes !== undefined) {
            const limit = y(Math.min(scaleMax, day.AllowanceMinutes + day.BonusMinutes));
            group.append(svg('line', { class: 'kidsLimit', x1: x - 3, x2: x + barWidth + 3, y1: limit, y2: limit }));
        }

        if (i % labelEvery === 0 || i === stats.Days.length - 1) {
            group.append(svg('text', { x: x + (barWidth / 2), y: height - 8, 'text-anchor': 'middle' }, String(parseDay(day.Day).getDate())));
        }

        root.append(group);
    });
    chart.replaceChildren(root);

    const items = [...keys.values()].map((key) => [key.colour, key.name]);
    if (usedOthers) {
        items.push([othersColour, t('others')]);
    }

    if (usedOutside) {
        items.push([outsideColour, t('notInLog')]);
    }

    const entries = items.map(([colour, name]) => {
        const item = element('li');
        const swatch = element('span', 'kidsSwatch');
        swatch.style.backgroundColor = colour;
        item.append(swatch, document.createTextNode(name));
        return item;
    });
    const limit = element('li');
    limit.append(element('span', 'kidsSwatchLine'), document.createTextNode(t('limit')));
    entries.push(limit);
    legend.replaceChildren(...entries);
}

function emptyText(stats) {
    return element('p', 'kidsEmpty', stats.AdvancedStats ? t('nothing') : t('needsAdvanced'));
}

function renderSeries(view, stats) {
    const box = view.querySelector('.kidsSeries');
    if (stats.Series.length === 0) {
        box.replaceChildren(emptyText(stats));
        return;
    }

    const table = element('table', 'kidsTable');
    const head = element('tr');
    [['series', ''], ['plays', 'kidsNum'], ['time', 'kidsNum'], ['last', '']].forEach(([key, cls]) => head.append(element('th', cls, t(key))));
    table.append(element('thead'));
    table.tHead.append(head);
    const body = element('tbody');
    for (const series of stats.Series) {
        const row = element('tr');
        const name = element('td', 'kidsName');
        const hasEpisodes = series.Episodes.length > 0;
        const open = state.open.has(series.Key);
        if (hasEpisodes) {
            const button = element('button', 'kidsExpand');
            button.type = 'button';
            button.setAttribute('aria-expanded', String(open));
            button.setAttribute('aria-label', t('expand', series.Name));
            button.append(element('span', 'material-icons ' + (open ? 'expand_more' : 'chevron_right')), element('span', null, series.Name));
            button.addEventListener('click', () => {
                if (state.open.has(series.Key)) {
                    state.open.delete(series.Key);
                } else {
                    state.open.add(series.Key);
                }

                renderSeries(view, stats);
                box.querySelector('[data-key="' + CSS.escape(series.Key) + '"] .kidsExpand').focus();
            });
            name.append(button);
        } else {
            name.append(element('span', null, series.Name));
        }

        if (series.IsLiveTv) {
            name.append(element('span', 'kidsLive', t('live')));
        }

        row.dataset.key = series.Key;
        row.append(name, element('td', 'kidsNum', String(series.Plays)), element('td', 'kidsNum', duration(series.Seconds)), element('td', null, lastSeen(series.LastWatched, stats.Today)));
        body.append(row);
        if (hasEpisodes && open) {
            for (const episode of series.Episodes) {
                const episodeRow = element('tr', 'kidsEpisode');
                const title = episode.Season !== null && episode.Season !== undefined && episode.Episode !== null && episode.Episode !== undefined
                    ? t('season', episode.Season, episode.Episode, episode.Name)
                    : episode.Name;
                episodeRow.append(element('td', 'kidsName', title), element('td', 'kidsNum', String(episode.Plays)), element('td', 'kidsNum', duration(episode.Seconds)), element('td', null, lastSeen(episode.LastWatched, stats.Today)));
                body.append(episodeRow);
            }
        }
    }

    table.append(body);
    box.replaceChildren(table);
}

function renderHours(view, stats) {
    const box = view.querySelector('.kidsHours');
    const rows = [[t('weekdays'), stats.Hours.Weekdays], [t('weekend'), stats.Hours.Weekend]];
    const max = Math.max(0, ...stats.Hours.Weekdays, ...stats.Hours.Weekend);
    if (max === 0) {
        box.replaceChildren(emptyText(stats));
        return;
    }

    const grid = element('div', 'kidsHeat');
    grid.setAttribute('role', 'table');
    grid.setAttribute('aria-label', t('hourTitle'));
    grid.append(element('span'));
    for (let hour = 0; hour < 24; hour++) {
        grid.append(element('span', 'kidsHeatHour', hour % 3 === 0 ? String(hour) : ''));
    }

    for (const [label, values] of rows) {
        grid.append(element('span', 'kidsHeatRow', label));
        values.forEach((seconds, hour) => {
            const cell = element('span', 'kidsHeatCell');
            const text = label + ' ' + String(hour).padStart(2, '0') + ':00 · ' + duration(seconds);
            cell.title = text;
            cell.setAttribute('role', 'cell');
            cell.setAttribute('aria-label', text);
            if (seconds > 0) {
                cell.style.opacity = String(0.2 + (0.8 * seconds / max));
            } else {
                cell.classList.add('kidsHeatEmpty');
            }

            grid.append(cell);
        });
    }

    box.replaceChildren(grid);
}

function renderDevices(view, stats) {
    const box = view.querySelector('.kidsDevices');
    if (stats.Devices.length === 0) {
        box.replaceChildren(emptyText(stats));
        return;
    }

    const max = Math.max(...stats.Devices.map((d) => d.Seconds), 1);
    const table = element('table', 'kidsTable');
    const head = element('tr');
    [['device', ''], ['app', ''], ['plays', 'kidsNum'], ['time', 'kidsNum']].forEach(([key, cls]) => head.append(element('th', cls, t(key))));
    head.append(element('th'));
    table.append(element('thead'));
    table.tHead.append(head);
    const body = element('tbody');
    for (const device of stats.Devices) {
        const row = element('tr');
        const barCell = element('td');
        barCell.style.width = '30%';
        const bar = element('div', 'kidsBar');
        bar.style.width = Math.round(100 * device.Seconds / max) + '%';
        barCell.append(bar);
        row.append(element('td', 'kidsName', device.Device === '?' ? t('unknown') : device.Device), element('td', null, device.Client || ''), element('td', 'kidsNum', String(device.Plays)), element('td', 'kidsNum', duration(device.Seconds)), barCell);
        body.append(row);
    }

    table.append(body);
    box.replaceChildren(table);
}

function renderStats(view, stats) {
    state.stats = stats;
    renderSummary(view, stats);
    const off = !stats.AdvancedStats;
    view.querySelector('.kidsOffNotice').hidden = !off;
    setText(view.querySelector('.kidsOffKept'), off && stats.Series.length > 0 ? t('offKept') : null);
    renderDays(view, stats);
    renderSeries(view, stats);
    renderHours(view, stats);
    renderDevices(view, stats);
}

function selectTab(view, tab, focus) {
    state.tab = tab;
    view.querySelectorAll('.kidsTab').forEach((button) => {
        const selected = button.dataset.tab === tab;
        button.setAttribute('aria-selected', String(selected));
        button.tabIndex = selected ? 0 : -1;
        if (selected && focus) {
            button.focus();
        }
    });
    view.querySelectorAll('.kidsPanel').forEach((panel) => {
        panel.hidden = panel.dataset.tab !== tab;
    });
}

function loadStats(view) {
    const kid = view.querySelector('.kidsStatsKid').value;
    if (!kid) {
        return Promise.resolve();
    }

    const days = view.querySelector('.kidsStatsRange').value;
    Dashboard.showLoadingMsg();
    showError(view, null);
    return request('GET', 'Kids/Users/' + kid + '/Stats/Detail?days=' + encodeURIComponent(days))
        .then((stats) => renderStats(view, stats))
        .catch(() => showError(view, t('loadFailed')))
        .finally(() => Dashboard.hideLoadingMsg());
}

function renderHeld(view, info) {
    const line = view.querySelector('.kidsHeld');
    const deleteButton = view.querySelector('.kidsDeleteLog');
    const empty = !info || (info.Segments === 0 && info.Breaks === 0);
    line.textContent = !info ? '' : empty ? t('heldEmpty') : t('held', info.Segments, info.Breaks, dayLabel(info.OldestDay, { day: 'numeric', month: 'long', year: 'numeric' }));
    // emby-button styles override the hidden attribute
    deleteButton.style.display = empty ? 'none' : '';
}

function loadSettings(view) {
    return request('GET', 'Kids/Admin/WatchLog').then((info) => {
        state.advanced = info.AdvancedStats;
        state.retention = info.RetentionDays;
        view.querySelector('.kidsAdvancedStats').checked = info.AdvancedStats;
        fillRetention(view);
        fillRanges(view);
        renderHeld(view, info);
    });
}

function loadKids(view) {
    return Promise.all([ApiClient.getUsers(), request('GET', 'Kids/Profiles')]).then(([users, kids]) => {
        const names = new Map(users.map((user) => [user.Id, user.Name]));
        state.kids = kids
            .map((kid) => ({ id: kid.UserId, name: names.get(kid.UserId) || kid.UserId }))
            .sort((a, b) => a.name.localeCompare(b.name));
        const select = view.querySelector('.kidsStatsKid');
        const current = select.value;
        select.replaceChildren(...state.kids.map((kid) => {
            const option = element('option', null, kid.name);
            option.value = kid.id;
            return option;
        }));
        if (state.kids.some((kid) => kid.id === current)) {
            select.value = current;
        }

        const none = state.kids.length === 0;
        view.querySelector('.kidsNoKids').hidden = !none;
        view.querySelector('.kidsBody').hidden = none;
    });
}

function load(view) {
    Dashboard.showLoadingMsg();
    showError(view, null);
    return loadSettings(view)
        .then(() => loadKids(view))
        .then(() => loadStats(view))
        .catch(() => showError(view, t('loadFailed')))
        .finally(() => Dashboard.hideLoadingMsg());
}

function saveSettings(view) {
    Dashboard.showLoadingMsg();
    updateConfiguration((config) => {
        config.AdvancedStats = view.querySelector('.kidsAdvancedStats').checked;
        config.StatsRetentionDays = Number(view.querySelector('.kidsRetention').value);
    })
        .then(() => {
            Dashboard.alert(t('saved'));
            return load(view);
        })
        .catch(() => {
            Dashboard.hideLoadingMsg();
            Dashboard.alert({ title: t('title'), message: escapeHtml(t('saveFailed')) });
        });
}

function deleteLog(view) {
    Dashboard.confirm({
        title: escapeHtml(t('deleteTitle')),
        text: escapeHtml(t('deleteText')),
        confirmText: escapeHtml(t('delete')),
        primary: 'delete'
    }, '', (confirmed) => {
        if (!confirmed) {
            return;
        }

        Dashboard.showLoadingMsg();
        request('DELETE', 'Kids/Admin/WatchLog')
            .then(() => {
                Dashboard.alert(t('deleted'));
                return load(view);
            })
            .catch(() => {
                Dashboard.hideLoadingMsg();
                Dashboard.alert({ title: t('title'), message: escapeHtml(t('deleteFailed')) });
            });
    });
}

export default function (view) {
    translatePage(view);
    view.querySelector('.kidsStatsKid').addEventListener('change', () => {
        state.open.clear();
        loadStats(view);
    });
    view.querySelector('.kidsStatsRange').addEventListener('change', () => loadStats(view));
    const tabs = [...view.querySelectorAll('.kidsTab')];
    tabs.forEach((button, i) => {
        button.addEventListener('click', () => selectTab(view, button.dataset.tab, false));
        // Arrow keys move between tabs, as for any tablist
        button.addEventListener('keydown', (event) => {
            const move = { ArrowRight: 1, ArrowLeft: -1 }[event.key];
            if (move) {
                event.preventDefault();
                selectTab(view, tabs[(i + move + tabs.length) % tabs.length].dataset.tab, true);
            }
        });
    });
    view.querySelector('.kidsSettingsForm').addEventListener('submit', (event) => {
        event.preventDefault();
        saveSettings(view);
    });
    view.querySelector('.kidsDeleteLog').addEventListener('click', () => deleteLog(view));
    view.addEventListener('viewshow', () => load(view));
}
