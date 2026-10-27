// The kid settings editor: what still leaves the kid unprotected in Jellyfin (with links to fix it), picked live TV
// channels that Jellyfin hides from them included; today's screen time with the server time, "Reset today" and, during
// a break, "End break"; the daily allowance and the sitting limit, each with an explicit "No limit", and the household
// break; bonus minutes and today's bonuses, with Undo inside their window; the allowed hours from Jellyfin
// (read-only); the avatar; search, autoplay and its limit; home sections (order and visibility); the picked
// collections and playlists; and the live TV channels the kid may watch. Saves with the loaded revision; a conflict
// never overwrites. Bonus minutes, Undo, End break and Reset today apply straight away, apart from the save. Every
// name is inserted as text; successes are confirmed with a toast.

const sectionNames = {
    KeepWatching: 'Keep watching',
    Picked: 'Picked by your grown-ups',
    Libraries: 'Libraries',
    New: 'New',
    Favourites: 'Favourites'
};

function request(type, path, body) {
    const options = { type, url: ApiClient.getUrl(path), dataType: 'json' };
    if (body !== undefined) {
        options.data = JSON.stringify(body);
        options.contentType = 'application/json';
    }

    // Rejects with the fetch Response for HTTP errors, so callers can read its status and body
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

function icon(name, className) {
    const node = element('span', 'material-icons ' + (className ? className + ' ' : '') + name);
    node.setAttribute('aria-hidden', 'true');
    return node;
}

// Jellyfin's dialogs (Dashboard.alert with an object, Dashboard.confirm) put their text in as HTML, so names in it are
// escaped; toasts use plain text
function escapeHtml(text) {
    const escapes = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', '\'': '&#39;' };
    return String(text).replace(/[&<>"']/g, (c) => escapes[c]);
}

function toast(text) {
    // A string is a toast; an object would be a modal
    Dashboard.alert(text);
}

function iconButton(name, label, onClick, disabled) {
    const button = element('button', 'listItemButton paper-icon-button-light');
    button.type = 'button';
    button.setAttribute('is', 'paper-icon-button-light');
    button.title = label;
    button.setAttribute('aria-label', label);
    button.disabled = Boolean(disabled);
    button.append(icon(name));
    button.addEventListener('click', onClick);
    return button;
}

function move(list, index, offset) {
    const target = index + offset;
    if (target >= 0 && target < list.length) {
        [list[index], list[target]] = [list[target], list[index]];
    }
}

function pickTypeName(type) {
    return type === 'Playlist' ? 'Playlist' : 'Collection';
}

function pickIcon(type) {
    return icon(type === 'Playlist' ? 'playlist_play' : 'video_library', 'listItemIcon');
}

// A list row that is not itself clickable: its controls are
function row(className) {
    const item = element('div', 'listItem kidsRow' + (className ? ' ' + className : ''));
    item.dataset.action = 'none';
    return item;
}

function setLine(node, text) {
    node.textContent = text || '';
    node.hidden = !text;
}

// Shared with kids.js (the two pages are separate modules): avatars, wording, health, live TV state and the server
// clock

// The avatar ids a profile can name. The pictures belong to the kids apps; the dashboard shows a coloured initial of
// its own
const avatarNames = { leo: 'Leo', max: 'Max', nico: 'Nico', lucy: 'Lucy', sara: 'Sara', mia: 'Mia' };
const avatarColours = { leo: '#1565c0', max: '#2e7d32', nico: '#6a1b9a', lucy: '#c62828', sara: '#ad1457', mia: '#00695c' };

function avatarName(id) {
    return avatarNames[id] || id;
}

function avatarInitial(id) {
    const name = avatarName(id);
    const node = element('span', 'kidsAvatarInitial', name.charAt(0).toUpperCase());
    node.style.backgroundColor = avatarColours[id] || '#546e7a';
    node.title = 'Avatar: ' + name;
    node.setAttribute('role', 'img');
    node.setAttribute('aria-label', 'Avatar: ' + name);
    return node;
}

function minutes(seconds) {
    return Math.floor(seconds / 60);
}

function usageText(status) {
    const used = minutes(status.UsedSeconds);
    if (status.AllowanceSeconds === undefined || status.AllowanceSeconds === null) {
        return 'Today: ' + used + ' min · No daily limit';
    }

    if (status.AllowanceSeconds === 0 && !(status.BonusSeconds > 0)) {
        return 'Today: ' + used + ' min · No screen time today';
    }

    const bonus = status.BonusSeconds > 0 ? ' + ' + minutes(status.BonusSeconds) + ' min bonus' : '';
    const of = 'Today: ' + used + ' of ' + minutes(status.AllowanceSeconds) + ' min' + bonus;
    return status.TimeUp ? of + ' · time is up' : of + ' · ' + minutes(status.RemainingSeconds) + ' min left';
}

// The current sitting, counted by the server across the kid's devices; null when the kid has no sitting limit
function sittingText(status) {
    if (status.SittingLimitSeconds === undefined || status.SittingLimitSeconds === null) {
        return null;
    }

    const of = 'This sitting: ' + minutes(status.SittingUsedSeconds) + ' of ' + minutes(status.SittingLimitSeconds) + ' min';
    if (status.BreakRemainingSeconds > 0) {
        return of + ' · on a break, ' + Math.ceil(status.BreakRemainingSeconds / 60) + ' min to go';
    }

    return of + ' · ' + minutes(status.SittingRemainingSeconds) + ' min left';
}

const deniedReasons = {
    timeUp: 'screen time is over for today',
    outsideSchedule: 'outside the allowed hours',
    sessionLimit: 'sitting limit reached'
};

// Present only while enforcement is on and the kid may not play; the end is shown on the server's clock
function deniedText(status, clock) {
    if (!status.DeniedReason) {
        return null;
    }

    const reason = deniedReasons[status.DeniedReason] || status.DeniedReason;
    let until = '';
    if (status.DeniedUntil) {
        until = clock
            ? ' until ' + clock.format(status.DeniedUntil) + ' (server time)'
            : ' until ' + new Date(status.DeniedUntil).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    }

    return 'Playback refused' + until + ': ' + reason + '.';
}

function userTab(userId, tab) {
    return '#/dashboard/users/' + userId + '/' + tab;
}

// Picked channels that Jellyfin's own parental control hides from the kid. The server flags them
// (Kids/Admin/LiveTvChannels?userId=), so the page never judges ratings itself; the rating and the policy only word
// the fix (feature 12, research R5). None when the flags are unknown or live TV access is off (its own line).
function hiddenChannelIssues(user, pickedIds, flagged) {
    const policy = user && user.Policy;
    if (!policy || !flagged || policy.EnableLiveTvAccess === false) {
        return [];
    }

    const picked = new Set(pickedIds || []);
    const blocksUnrated = (policy.BlockUnratedItems || []).includes('LiveTvChannel');
    const hasMaximum = policy.MaxParentalRating !== undefined && policy.MaxParentalRating !== null;
    const untick = 'Untick Live TV under "Block items with no or unrecognized rating information"';
    return flagged.filter((channel) => channel.HiddenFromUser && picked.has(channel.Id)).map((channel) => {
        const hidden = (channel.Name || channel.Id) + ' is hidden from ' + user.Name;
        let text;
        if (!channel.Rating && blocksUnrated) {
            text = hidden + ': unrated live TV channels are blocked. ' + untick + ', or give the channel a rating.';
        } else if (channel.Rating && hasMaximum) {
            text = hidden + ': it is rated ' + channel.Rating + ', above the maximum allowed rating. Raise the maximum or change the channel\'s rating.';
        } else if (channel.Rating && blocksUnrated) {
            text = hidden + ': Jellyfin does not recognise its rating ' + channel.Rating + ', and unrated live TV channels are blocked. '
                + untick + ', or change the channel\'s rating.';
        } else {
            text = hidden + ' by Jellyfin\'s parental control, for example a blocked or missing tag. Check the Parental control tab.';
        }

        return { text, href: userTab(user.Id, 'parentalcontrol') };
    });
}

// What still leaves a kid unprotected in their Jellyfin policy, each with where to fix it (research R2). Null when
// the policy is unknown: never "no issue" then. In the editor the channel list is on the page, so "none picked" has
// no link. flagged is the server's channel list for the kid, or null.
function healthIssues(user, pickedIds, serverChannels, flagged) {
    const policy = user && user.Policy;
    if (!policy) {
        return null;
    }

    const issues = [];
    if (policy.IsAdministrator) {
        issues.push({ text: 'Administrator: bypasses every restriction. Use a non-admin account for this kid.', href: userTab(user.Id, 'profile') });
    }

    if (policy.EnableAllFolders) {
        issues.push({ text: 'Sees every library', href: userTab(user.Id, 'access') });
    }

    if (policy.MaxParentalRating === undefined || policy.MaxParentalRating === null) {
        issues.push({ text: 'No maximum parental rating', href: userTab(user.Id, 'parentalcontrol') });
    }

    const blocked = policy.BlockUnratedItems || [];
    if (!blocked.includes('Movie') || !blocked.includes('Series')) {
        issues.push({ text: 'Unrated movies and shows are not blocked', href: userTab(user.Id, 'parentalcontrol') });
    }

    const picked = (pickedIds || []).length;
    if (policy.EnableLiveTvAccess === false && picked > 0) {
        issues.push({ text: 'Live TV access is off in Jellyfin, so the picked channels don\'t play', href: userTab(user.Id, 'profile') });
    } else if (policy.EnableLiveTvAccess !== false && picked === 0 && serverChannels && serverChannels.length > 0) {
        issues.push({ text: 'Live TV access is on but no channels are picked', href: null });
    }

    issues.push(...hiddenChannelIssues(user, pickedIds, flagged));

    if (policy.EnableContentDownloading) {
        issues.push({ text: 'Downloads are allowed', href: userTab(user.Id, 'profile') });
    }

    return issues;
}

// "Watches N of M channels" or "No live TV"; without the server's list, "Watches N channels"
function liveTvText(user, pickedIds, serverChannels) {
    const ids = pickedIds || [];
    if (user && user.Policy && user.Policy.EnableLiveTvAccess === false) {
        return 'No live TV';
    }

    const count = (n) => n + (n === 1 ? ' channel' : ' channels');
    if (!serverChannels) {
        return ids.length === 0 ? 'No live TV' : 'Watches ' + count(ids.length);
    }

    const known = new Set(serverChannels.map((channel) => channel.Id));
    const picked = ids.filter((id) => known.has(id)).length;
    return picked === 0 ? 'No live TV' : 'Watches ' + picked + ' of ' + count(serverChannels.length);
}

function pad(n) {
    return String(n).padStart(2, '0');
}

// The server's clock from a Status: NextDayStartsAt is the day-start hour in the server's zone, which gives its UTC
// offset (research R5) without an API change. Null without a usable status.
function serverClock(status, dayStartHour) {
    const next = status && Date.parse(status.NextDayStartsAt);
    const now = status && Date.parse(status.ServerTime);
    if (!Number.isFinite(next) || !Number.isFinite(now) || !Number.isInteger(dayStartHour)) {
        return null;
    }

    const nextDate = new Date(next);
    let offset = (((dayStartHour * 60 - (nextDate.getUTCHours() * 60 + nextDate.getUTCMinutes())) % 1440) + 1440) % 1440;
    if (offset > 840) {
        offset -= 1440;
    }

    const format = (value) => {
        const shifted = new Date(Date.parse(value) + offset * 60000);
        return pad(shifted.getUTCHours()) + ':' + pad(shifted.getUTCMinutes());
    };
    const size = Math.abs(offset);
    const label = 'UTC' + (offset < 0 ? '−' : '+') + pad(Math.floor(size / 60)) + ':' + pad(size % 60);
    return { format, label, now: format(status.ServerTime) };
}

// Jellyfin's allowed hours (AccessSchedules), read-only: hours are decimal (8.5 is 08:30)
const scheduleDays = {
    Everyday: 'Every day',
    Weekday: 'Weekdays',
    Weekend: 'Weekends',
    Sunday: 'Sunday',
    Monday: 'Monday',
    Tuesday: 'Tuesday',
    Wednesday: 'Wednesday',
    Thursday: 'Thursday',
    Friday: 'Friday',
    Saturday: 'Saturday'
};
const scheduleDayOrder = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Everyday', 'Weekday', 'Weekend'];

function hourText(hours) {
    const total = Math.round(Number(hours) * 60);
    return pad(Math.floor(total / 60)) + ':' + pad(total % 60);
}

function scheduleText(schedule) {
    const day = typeof schedule.DayOfWeek === 'number' ? scheduleDayOrder[schedule.DayOfWeek] : schedule.DayOfWeek;
    return (scheduleDays[day] || String(day)) + ' ' + hourText(schedule.StartHour) + '–' + hourText(schedule.EndHour);
}

// The plugin id is looked up by name, so this anonymously served file holds no id
function pluginConfiguration() {
    return ApiClient.getJSON(ApiClient.getUrl('Plugins'))
        .then((plugins) => {
            const plugin = plugins.find((candidate) => candidate.Name === 'Kids');
            if (!plugin) {
                throw new Error('The Kids plugin is not listed');
            }

            return ApiClient.getPluginConfiguration(plugin.Id);
        });
}

export default function (view, params) {
    const userId = params.userId;
    const form = view.querySelector('.kidsUserForm');
    const capInput = view.querySelector('.kidsAutoplayCap');
    const autoplayInput = view.querySelector('.kidsAutoplayEnabled');
    const searchInput = view.querySelector('.kidsPickSearch');
    const results = view.querySelector('.kidsPickResults');
    const avatarSelect = view.querySelector('.kidsAvatar');

    // Editor state, filled from the server profile
    let revision = 0;
    let sections = [];
    let picks = []; // { Id, Name, Type, missing }
    let serverChannels = null; // the server's live TV channels, or null when they could not be read
    let channels = []; // { Id, Name, Number, ImageTag, Type, missing, allowed }
    let conflictProfile = null;
    let searchTimer = null;
    let user = null;
    let config = null; // the plugin configuration, for the day start and the break; null when unreadable
    let snapshot = null; // what Save would send, as loaded or last saved

    function showMessage(text) {
        const message = view.querySelector('.kidsMessage');
        message.textContent = text || '';
        message.hidden = !text;
        if (text) {
            message.append(' ');
            const back = element('a', 'button-link', 'Back to Kids');
            back.href = '#/configurationpage?name=Kids';
            message.append(back);
        }
    }

    function clearErrors() {
        view.querySelectorAll('.kidsError').forEach((node) => {
            node.textContent = '';
        });
        const formError = view.querySelector('.kidsFormError');
        formError.textContent = '';
        formError.hidden = true;
    }

    function showFormError(text) {
        const formError = view.querySelector('.kidsFormError');
        formError.textContent = text;
        formError.hidden = false;
    }

    function showErrors(errors) {
        clearErrors();
        for (const [key, messages] of Object.entries(errors || {})) {
            const match = /^(\w+)(?:\[(\d+)\])?$/.exec(key);
            const field = match ? match[1] : key;
            const target = view.querySelector('.kidsError[data-error-for="' + field + '"]');
            const text = (match && match[2] !== undefined ? 'Item ' + (Number(match[2]) + 1) + ': ' : '') + messages.join(' ');
            if (target) {
                target.textContent = (target.textContent ? target.textContent + ' ' : '') + text;
            } else {
                showFormError(text);
            }
        }
    }

    function capIsValid() {
        const value = Number(capInput.value);
        return Number.isInteger(value) && value >= 1 && value <= 10;
    }

    // A whole-minutes limit with an explicit "No limit" checkbox: value() is null when the box is ticked, the minutes
    // when in range, or undefined when invalid (an empty field included); render() shows or clears the range message
    // and says whether it is valid
    function limitField(selector, noLimitSelector, field, min, max, onChange) {
        const input = view.querySelector(selector);
        const noLimit = view.querySelector(noLimitSelector);
        const problem = 'Between ' + min + ' and ' + max + ' minutes.';
        const value = () => {
            if (noLimit.checked) {
                return null;
            }

            const text = input.value.trim();
            const minutes = Number(text);
            return text !== '' && Number.isInteger(minutes) && minutes >= min && minutes <= max ? minutes : undefined;
        };
        const render = () => {
            input.disabled = noLimit.checked;
            // A number input reports bad input (such as "abc") as an empty value with validity.badInput
            const invalid = !noLimit.checked && (input.validity.badInput || value() === undefined);
            view.querySelector('.kidsError[data-error-for="' + field + '"]').textContent = invalid ? problem : '';
            input.setAttribute('aria-invalid', String(invalid));
            if (onChange) {
                onChange(value());
            }

            return !invalid;
        };
        const load = (minutes) => {
            noLimit.checked = minutes === undefined || minutes === null;
            input.value = noLimit.checked ? '' : String(minutes);
            input.disabled = noLimit.checked;
            view.querySelector('.kidsError[data-error-for="' + field + '"]').textContent = '';
            input.setAttribute('aria-invalid', 'false');
            if (onChange) {
                onChange(value());
            }
        };
        input.addEventListener('input', render);
        noLimit.addEventListener('change', () => {
            render();
            if (!noLimit.checked) {
                input.focus();
            }
        });
        return { value, render, load };
    }

    function renderAllowanceHelp(minutes) {
        view.querySelector('.kidsAllowanceHelp').textContent = minutes === 0
            ? '0 minutes: No screen time today.'
            : 'Screen time a day, counted on every app. 0 means no screen time today.';
    }

    const allowance = limitField('.kidsDailyAllowance', '.kidsNoAllowance', 'DailyAllowanceMinutes', 0, 1440, renderAllowanceHelp);
    const sessionLimit = limitField('.kidsSessionLimit', '.kidsNoSessionLimit', 'SessionLimitMinutes', 5, 120);

    // The household break, set on the Kids page: it ends a sitting, and is the wait after the sitting limit
    function renderBreak() {
        const line = view.querySelector('.kidsBreakLine');
        const breakMinutes = Number(config && config.SittingBreakMinutes);
        const valid = Number.isInteger(breakMinutes) && breakMinutes >= 1 && breakMinutes <= 240;
        line.replaceChildren(
            (valid ? 'Break between sittings: ' + breakMinutes + ' min, set for the household on the ' : 'Break between sittings: set for the household on the '));
        const link = element('a', 'button-link', 'Kids page');
        link.setAttribute('is', 'emby-linkbutton');
        link.href = '#/configurationpage?name=Kids';
        line.append(link, '.');
    }

    // Unsaved changes: what Save would send, compared with what was loaded or last saved
    function currentBody() {
        return {
            SearchAllowed: view.querySelector('.kidsSearchAllowed').checked,
            AutoplayEnabled: autoplayInput.checked,
            AutoplayCap: Number(capInput.value),
            Sections: sections.map((entry) => ({ Section: entry.Section, Visible: entry.Visible })),
            PickedItemIds: picks.map((pick) => pick.Id),
            // Always sent: the update is a full replacement, and omitting them would clear them
            DailyAllowanceMinutes: allowance.value(),
            SessionLimitMinutes: sessionLimit.value(),
            LiveTvChannelIds: channels.filter((channel) => channel.allowed).map((channel) => channel.Id),
            Avatar: avatarSelect.value || null
        };
    }

    function isDirty() {
        return snapshot !== null && !form.hidden && JSON.stringify(currentBody()) !== snapshot;
    }

    function renderDirty() {
        view.querySelector('.kidsUnsaved').hidden = !isDirty();
    }

    function takeSnapshot() {
        snapshot = JSON.stringify(currentBody());
        renderDirty();
    }

    // The avatar: a stored id this page does not know (a later plugin's) is kept as an extra option, so saving the
    // other settings sends it back unchanged
    function renderAvatar() {
        const preview = view.querySelector('.kidsAvatarPreview');
        preview.replaceChildren(...(avatarSelect.value ? [avatarInitial(avatarSelect.value)] : []));
    }

    function loadAvatar(id) {
        avatarSelect.querySelectorAll('option[data-extra]').forEach((option) => option.remove());
        if (id && ![...avatarSelect.options].some((option) => option.value === id)) {
            const option = element('option', null, id);
            option.value = id;
            option.dataset.extra = 'true';
            avatarSelect.append(option);
        }

        avatarSelect.value = id || '';
        renderAvatar();
    }

    function renderCap() {
        capInput.disabled = !autoplayInput.checked;
        view.querySelector('.kidsError[data-error-for="AutoplayCap"]').textContent =
            autoplayInput.checked && !capIsValid() ? 'Choose a number from 1 to 10.' : '';
    }

    function renderSections() {
        const list = view.querySelector('.kidsSections');
        list.replaceChildren(...sections.map((entry, index) => {
            const item = row('listItem-border');
            const label = element('label');
            const checkbox = element('input');
            checkbox.type = 'checkbox';
            checkbox.setAttribute('is', 'emby-checkbox');
            checkbox.checked = entry.Visible;
            checkbox.addEventListener('change', () => {
                entry.Visible = checkbox.checked;
                renderDirty();
            });
            label.append(checkbox, element('span', null, sectionNames[entry.Section] || entry.Section));
            item.append(
                label,
                iconButton('arrow_upward', 'Move up', () => { move(sections, index, -1); renderSections(); }, index === 0),
                iconButton('arrow_downward', 'Move down', () => { move(sections, index, 1); renderSections(); }, index === sections.length - 1));
            return item;
        }));
        renderDirty();
    }

    function renderPicks() {
        const list = view.querySelector('.kidsPicks');
        renderDirty();
        if (picks.length === 0) {
            list.replaceChildren(element('div', 'fieldDescription kidsEmpty', 'Nothing picked yet.'));
            return;
        }

        list.replaceChildren(...picks.map((pick, index) => {
            const item = row('listItem-border');
            const body = element('div', 'listItemBody two-line');
            body.append(element('div', 'listItemBodyText', pick.missing ? 'Missing (deleted from the server)' : pick.Name));
            body.append(element('div', 'listItemBodyText secondary', pick.missing ? pick.Id : pickTypeName(pick.Type)));
            item.append(
                pickIcon(pick.Type),
                body,
                iconButton('arrow_upward', 'Move up', () => { move(picks, index, -1); renderPicks(); }, index === 0),
                iconButton('arrow_downward', 'Move down', () => { move(picks, index, 1); renderPicks(); }, index === picks.length - 1),
                iconButton('delete', 'Remove', () => { picks.splice(index, 1); renderPicks(); }));
            return item;
        }));
    }

    // Live TV: stored channels that are gone from the server come first, still ticked; then every server channel
    function mergeChannels(allowedIds) {
        const allowed = new Set(allowedIds);
        const known = new Set((serverChannels || []).map((channel) => channel.Id));
        const missing = allowedIds
            .filter((id) => !known.has(id))
            .map((id) => ({ Id: id, Name: '', Number: null, ImageTag: null, Type: 'TV', missing: true, allowed: true }));
        channels = missing.concat((serverChannels || []).map((channel) => Object.assign({}, channel, { missing: false, allowed: allowed.has(channel.Id) })));
    }

    function channelLogo(channel) {
        if (channel.ImageTag) {
            const image = element('img', 'kidsChannelLogo');
            image.alt = '';
            image.loading = 'lazy';
            image.src = ApiClient.getScaledImageUrl(channel.Id, { type: 'Primary', tag: channel.ImageTag, maxHeight: 64 });
            return image;
        }

        return icon(channel.Type === 'Radio' ? 'radio' : 'live_tv', 'kidsChannelIcon');
    }

    function renderLiveTvNotice() {
        const allowed = channels.filter((channel) => channel.allowed).map((channel) => channel.Id);
        view.querySelector('.kidsLiveTvNone').hidden = allowed.length > 0;
        view.querySelector('.kidsLiveTvState').textContent = liveTvText(user, allowed, serverChannels);
    }

    function renderChannels() {
        const list = view.querySelector('.kidsChannels');
        renderLiveTvNotice();
        renderDirty();
        if (serverChannels === null && channels.length === 0) {
            list.replaceChildren(element('div', 'fieldDescription kidsEmpty', 'Could not load the live TV channels.'));
            return;
        }

        if (channels.length === 0) {
            list.replaceChildren(element('div', 'fieldDescription kidsEmpty', 'This server has no live TV channels.'));
            return;
        }

        list.replaceChildren(...channels.map((channel) => {
            const item = row('listItem-border');
            const label = element('label');
            const checkbox = element('input');
            checkbox.type = 'checkbox';
            checkbox.setAttribute('is', 'emby-checkbox');
            checkbox.checked = channel.allowed;
            checkbox.addEventListener('change', () => {
                channel.allowed = checkbox.checked;
                renderLiveTvNotice();
                renderDirty();
            });
            const text = element('span');
            text.append(channelLogo(channel), channel.missing ? 'Missing (removed from the server)' : channel.Name);
            if (channel.missing || channel.Number) {
                text.append(element('span', 'kidsChannelNumber', channel.missing ? channel.Id : channel.Number));
            }

            label.append(checkbox, text);
            item.append(label);
            return item;
        }));
    }

    // Warnings from the kid's Jellyfin policy: live TV access is all or nothing, and unrated live TV may be blocked
    function renderLiveTvWarnings() {
        const policy = (user && user.Policy) || {};
        view.querySelector('.kidsLiveTvAccessOff').hidden = policy.EnableLiveTvAccess !== false;
        const blocked = policy.BlockUnratedItems || [];
        view.querySelector('.kidsLiveTvUnrated').hidden = !blocked.includes('LiveTvChannel') && !blocked.includes('LiveTvProgram');
        view.querySelector('.kidsUserProfileLink').href = userTab(userId, 'profile');
    }

    // Health lines from the stored channel list, the administrator one first
    function renderHealth(pickedIds) {
        const list = view.querySelector('.kidsHealthList');
        // The server's list for this kid carries the hidden flags, which follow Jellyfin's policy, not the picks
        const issues = healthIssues(user, pickedIds, serverChannels, serverChannels) || [];
        list.replaceChildren(...issues.map((issue) => {
            const line = element('p', 'kidsWarning');
            line.append(icon('warning'), issue.text);
            if (issue.href) {
                const fix = element('a', 'button-link', 'Fix');
                fix.setAttribute('is', 'emby-linkbutton');
                fix.href = issue.href;
                fix.setAttribute('aria-label', 'Fix: ' + issue.text);
                line.append(fix);
            }

            return line;
        }));
    }

    function renderSchedule() {
        const schedules = (user && user.Policy && user.Policy.AccessSchedules) || [];
        const list = view.querySelector('.kidsSchedule');
        list.replaceChildren(...(schedules.length === 0
            ? [element('li', null, 'Any time: no allowed hours are set in Jellyfin.')]
            : schedules.map((schedule) => element('li', null, scheduleText(schedule)))));
        view.querySelector('.kidsScheduleLink').href = userTab(userId, 'parentalcontrol');
    }

    function renderResults(candidates, term) {
        if (candidates.length === 0) {
            results.replaceChildren(element(
                'div',
                'fieldDescription kidsEmpty',
                term ? 'No collections or playlists match.' : 'This server has no collections or playlists yet.'));
            return;
        }

        results.replaceChildren(...candidates.map((candidate) => {
            const picked = picks.some((pick) => pick.Id === candidate.Id);
            const item = row();
            const body = element('div', 'listItemBody two-line');
            body.append(element('div', 'listItemBodyText', candidate.Name));
            body.append(element('div', 'listItemBodyText secondary', pickTypeName(candidate.Type) + (picked ? ' · already picked' : '')));
            item.append(pickIcon(candidate.Type), body, iconButton('add', 'Add ' + candidate.Name, () => {
                if (!picks.some((pick) => pick.Id === candidate.Id)) {
                    picks.push({ Id: candidate.Id, Name: candidate.Name, Type: candidate.Type, missing: false });
                    renderPicks();
                    renderResults(candidates, term);
                }
            }, picked));
            return item;
        }));
    }

    function search() {
        const term = searchInput.value.trim();
        const query = term ? '?limit=50&search=' + encodeURIComponent(term) : '?limit=50';
        request('GET', 'Kids/Admin/PickCandidates' + query)
            .then((candidates) => {
                // Ignore answers to an older search
                if (searchInput.value.trim() === term) {
                    renderResults(candidates, term);
                }
            })
            .catch(() => results.replaceChildren(element('div', 'fieldDescription kidsEmpty', 'Could not search the library.')));
    }

    function applyProfile(profile) {
        revision = profile.Revision;
        view.querySelector('.kidsSearchAllowed').checked = profile.SearchAllowed;
        autoplayInput.checked = profile.AutoplayEnabled;
        capInput.value = profile.AutoplayCap;
        allowance.load(profile.DailyAllowanceMinutes);
        sessionLimit.load(profile.SessionLimitMinutes);
        loadAvatar(profile.Avatar);
        sections = profile.Sections.map((entry) => ({ Section: entry.Section, Visible: entry.Visible }));
        picks = profile.PickedItemIds.map((id) => ({ Id: id, Name: '', Type: null, missing: true }));
        // Older plugins have no live TV field; the editor then shows the server's channels unticked
        mergeChannels(profile.LiveTvChannelIds || []);
        clearErrors();
        view.querySelector('.kidsConflict').hidden = true;
        renderCap();
        renderSections();
        renderPicks();
        renderChannels();
        renderHealth(profile.LiveTvChannelIds || []);
        takeSnapshot();

        if (picks.length === 0) {
            return Promise.resolve();
        }

        // Stored picks keep their id even when the item was deleted; resolve names and flag the missing ones
        return request('GET', 'Kids/Admin/PickCandidates?ids=' + picks.map((pick) => pick.Id).join(','))
            .then((found) => {
                const byId = new Map(found.map((candidate) => [candidate.Id, candidate]));
                picks = picks.map((pick) => {
                    const candidate = byId.get(pick.Id);
                    return candidate ? { Id: pick.Id, Name: candidate.Name, Type: candidate.Type, missing: false } : pick;
                });
                renderPicks();
            });
    }

    const usageLine = view.querySelector('.kidsUsageLine');
    const bonusInput = view.querySelector('.kidsBonusMinutes');
    const bonusAdd = view.querySelector('.kidsBonusAdd');
    const bonusError = view.querySelector('.kidsBonusError');
    const resetButton = view.querySelector('.kidsReset');
    const endBreakButton = view.querySelector('.kidsEndBreak');
    const bonusList = view.querySelector('.kidsBonusList');
    let clock = null; // the server's clock from the last status, for the bonus times

    function setBonusError(text) {
        bonusError.textContent = text || '';
        bonusError.hidden = !text;
    }

    function renderStatus(status) {
        clock = serverClock(status, config ? Number(config.DayStartsAtHour) : Number.NaN);
        usageLine.textContent = usageText(status);
        setLine(view.querySelector('.kidsSittingLine'), sittingText(status));
        setLine(view.querySelector('.kidsDeniedLine'), deniedText(status, clock));
        view.querySelector('.kidsAppOnlyLine').hidden = status.Enforced !== false;
        setLine(view.querySelector('.kidsServerTimeLine'), clock ? 'Server time: ' + clock.now + ' (' + clock.label + ').' : null);
        endBreakButton.hidden = !(status.BreakRemainingSeconds > 0);
        // Every change to the status can change today's bonuses (add, undo, reset)
        loadBonuses();
    }

    function timeText(value) {
        return clock ? clock.format(value) : new Date(value).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    }

    // Today's bonuses (feature 13), newest first, with Undo while the server still accepts it
    function renderBonuses(list) {
        bonusList.replaceChildren();
        const bonuses = (list && list.Bonuses) || [];
        bonusList.hidden = bonuses.length === 0;
        if (bonuses.length === 0) {
            return;
        }

        bonusList.append(element('p', 'fieldDescription', 'Today\'s bonuses'));
        for (const bonus of bonuses) {
            const line = element('p', 'fieldDescription kidsBonusItem',
                bonus.Minutes + ' min at ' + timeText(bonus.CreatedAt) + ' by ' + bonus.GrantedBy + (bonus.CanUndo ? '' : ' · can no longer be undone'));
            if (bonus.CanUndo) {
                const undo = element('button', 'button-link kidsBonusUndo', 'Undo');
                undo.type = 'button';
                undo.setAttribute('is', 'emby-button');
                undo.setAttribute('aria-label', 'Undo ' + bonus.Minutes + ' min bonus');
                undo.addEventListener('click', () => undoBonus(bonus, undo));
                line.append(undo);
            }

            bonusList.append(line);
        }
    }

    function loadBonuses() {
        return request('GET', 'Kids/Users/' + userId + '/WatchTime/Bonus')
            .then(renderBonuses)
            .catch(() => renderBonuses(null));
    }

    function undoBonus(bonus, button) {
        button.disabled = true;
        request('DELETE', 'Kids/Users/' + userId + '/WatchTime/Bonus/' + bonus.Id)
            .then((status) => {
                renderStatus(status);
                toast('Undid the ' + bonus.Minutes + ' min bonus');
            })
            .catch((response) => {
                const code = response && response.status;
                if (code === 409) {
                    Dashboard.alert({ title: 'Kids', message: 'This bonus can no longer be undone.' });
                } else if (code !== 404) {
                    // 404: someone else undid it already; the reload shows it
                    Dashboard.alert({ title: 'Kids', message: 'Could not undo the bonus.' });
                }

                return loadUsage();
            });
    }

    function endBreak() {
        const name = user ? user.Name : 'this kid';
        endBreakButton.disabled = true;
        request('POST', 'Kids/Users/' + userId + '/WatchTime/Break/End')
            .then((status) => {
                renderStatus(status);
                toast(name + '\'s break was ended');
            })
            .catch(() => Dashboard.alert({ title: 'Kids', message: 'Could not end the break.' }))
            .finally(() => {
                endBreakButton.disabled = false;
            });
    }

    function loadUsage() {
        usageLine.textContent = 'Today: …';
        setLine(view.querySelector('.kidsSittingLine'), null);
        setLine(view.querySelector('.kidsDeniedLine'), null);
        view.querySelector('.kidsAppOnlyLine').hidden = true;
        setLine(view.querySelector('.kidsServerTimeLine'), null);
        endBreakButton.hidden = true;
        // Never show 0 when the status could not be read
        return request('GET', 'Kids/Users/' + userId + '/Status')
            .then(renderStatus)
            .catch(() => {
                usageLine.textContent = 'Usage unavailable';
            });
    }

    function addBonus() {
        setBonusError(null);
        const value = Number(bonusInput.value.trim());
        if (bonusInput.value.trim() === '' || !Number.isInteger(value) || value < 1 || value > 240) {
            setBonusError('Between 1 and 240 minutes.');
            return;
        }

        bonusAdd.disabled = true;
        request('POST', 'Kids/Users/' + userId + '/WatchTime/Bonus', { Minutes: value })
            .then((status) => {
                bonusInput.value = '';
                renderStatus(status);
                toast('Added ' + value + ' min for today');
            })
            .catch((response) => {
                if (response && response.status === 400 && typeof response.json === 'function') {
                    return response.json().then((problemDetails) => {
                        const errors = (problemDetails && problemDetails.errors) || {};
                        setBonusError(errors.Minutes ? errors.Minutes[0] : 'The bonus was refused.');
                    }, () => setBonusError('The bonus was refused.'));
                }

                Dashboard.alert({ title: 'Kids', message: 'Could not add bonus time.' });
                return undefined;
            })
            .finally(() => {
                bonusAdd.disabled = false;
            });
    }

    function resetToday() {
        const name = user ? user.Name : 'this kid';
        Dashboard.confirm({
            title: 'Reset today',
            text: 'Set ' + escapeHtml(name) + '\'s screen time and bonus for today back to 0? If they were stopped for time or the sitting limit, they can watch again at once.',
            confirmText: 'Reset',
            primary: 'delete'
        }, '', (confirmed) => {
            if (!confirmed) {
                return;
            }

            resetButton.disabled = true;
            request('POST', 'Kids/Users/' + userId + '/WatchTime/Reset')
                .then((status) => {
                    renderStatus(status);
                    toast('Today\'s screen time for ' + name + ' was reset');
                })
                .catch(() => Dashboard.alert({ title: 'Kids', message: 'Could not reset today\'s screen time.' }))
                .finally(() => {
                    resetButton.disabled = false;
                });
        });
    }

    function load() {
        Dashboard.showLoadingMsg();
        showMessage(null);
        form.hidden = true;
        snapshot = null;
        // The channel list and the configuration are not needed to edit the rest: if they fail, their lines say less
        // Asked for this kid, so each channel also says whether Jellyfin hides it from them
        const liveTvChannels = request('GET', 'Kids/Admin/LiveTvChannels?userId=' + encodeURIComponent(userId)).catch(() => null);
        const household = pluginConfiguration().catch(() => null);
        return Promise.all([request('GET', 'Kids/Users/' + userId + '/Profile'), ApiClient.getUser(userId), liveTvChannels, household])
            .then(([profile, loadedUser, serverList, loadedConfig]) => {
                user = loadedUser;
                config = loadedConfig;
                view.querySelector('.kidsUserTitle').textContent = 'Kid settings: ' + user.Name;
                serverChannels = serverList;
                renderLiveTvWarnings();
                renderSchedule();
                renderBreak();
                return applyProfile(profile).then(() => {
                    form.hidden = false;
                    renderDirty();
                    loadUsage();
                    search();
                });
            })
            .catch((response) => {
                showMessage(response && response.status === 404 ? 'This user is not a kid.' : 'Could not load the kid settings.');
            })
            .finally(() => Dashboard.hideLoadingMsg());
    }

    function save(event) {
        event.preventDefault();
        clearErrors();
        if (!capIsValid()) {
            renderCap();
            view.querySelector('.kidsError[data-error-for="AutoplayCap"]').textContent = 'Choose a number from 1 to 10.';
            return false;
        }

        // Check both, so each shows its message
        const allowanceValid = allowance.render();
        if (!sessionLimit.render() || !allowanceValid) {
            return false;
        }

        const body = Object.assign({ Revision: revision }, currentBody());
        Dashboard.showLoadingMsg();
        request('PUT', 'Kids/Users/' + userId + '/Profile', body)
            .then((profile) => {
                revision = profile.Revision;
                takeSnapshot();
                renderHealth(profile.LiveTvChannelIds || []);
                toast('Saved');
                // The status follows the new allowance and sitting limit
                return loadUsage();
            })
            .catch((response) => {
                const status = response && response.status;
                if (status === 409) {
                    return response.json().then((current) => {
                        conflictProfile = current;
                        const banner = view.querySelector('.kidsConflict');
                        banner.hidden = false;
                        // The save button is at the bottom of a long form; bring the banner into view
                        banner.scrollIntoView({ block: 'center' });
                    });
                }

                if (status === 400) {
                    return response.json().then((problem) => showErrors(problem.errors || problem.Errors));
                }

                if (status === 404) {
                    form.hidden = true;
                    showMessage('This user is no longer a kid.');
                    return undefined;
                }

                showFormError('Could not save. Nothing was changed.');
                return undefined;
            })
            .finally(() => Dashboard.hideLoadingMsg());
        return false;
    }

    // Reloading or closing the tab with unsaved changes asks first; Jellyfin's own navigation cannot be held back
    const onBeforeUnload = (event) => {
        if (isDirty()) {
            event.preventDefault();
            event.returnValue = '';
        }
    };

    form.addEventListener('submit', save);
    form.addEventListener('input', renderDirty);
    form.addEventListener('change', renderDirty);
    bonusAdd.addEventListener('click', addBonus);
    bonusInput.addEventListener('keydown', (event) => {
        // Enter adds the bonus instead of saving the whole form
        if (event.key === 'Enter') {
            event.preventDefault();
            addBonus();
        }
    });
    resetButton.addEventListener('click', resetToday);
    endBreakButton.addEventListener('click', endBreak);
    autoplayInput.addEventListener('change', renderCap);
    avatarSelect.addEventListener('change', renderAvatar);
    capInput.addEventListener('input', renderCap);
    searchInput.addEventListener('input', () => {
        clearTimeout(searchTimer);
        searchTimer = setTimeout(search, 300);
    });
    view.querySelector('.kidsReload').addEventListener('click', () => {
        if (conflictProfile) {
            applyProfile(conflictProfile);
            conflictProfile = null;
        }
    });
    view.addEventListener('viewshow', () => {
        window.addEventListener('beforeunload', onBeforeUnload);
        load();
    });
    view.addEventListener('viewhide', () => window.removeEventListener('beforeunload', onBeforeUnload));
}
