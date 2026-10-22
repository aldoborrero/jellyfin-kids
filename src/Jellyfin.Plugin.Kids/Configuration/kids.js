// The Kids dashboard page: a banner when limits only apply in Wholphin or some routes cannot be guarded; the list of
// Jellyfin users where kids are marked and unmarked, each kid with today's screen time, their live TV, what still leaves
// them unprotected in Jellyfin (with a link to fix it) and an "App only" pill while enforcement is off; the household
// parent lock; and the screen time settings: "day starts at" with the server time, enforcement on every app with its
// self-check, and the break between sittings.
// Data comes only from authenticated calls; the page HTML is served anonymously and holds none. Every name is inserted
// as text. Successes are confirmed with a toast, errors inline or in a dialog.

function request(type, path, body) {
    // DELETE answers 204 without a body, which ApiClient cannot parse as JSON
    const options = { type, url: ApiClient.getUrl(path) };
    if (type !== 'DELETE') {
        options.dataType = 'json';
    }

    if (body !== undefined) {
        options.data = JSON.stringify(body);
        options.contentType = 'application/json';
    }

    // Rejects with the fetch Response for HTTP errors, so callers can read its status
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

function icon(name) {
    const node = element('span', 'material-icons ' + name);
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

function showError(view, message) {
    const error = view.querySelector('.kidsError');
    error.textContent = message || '';
    error.style.display = message ? '' : 'none';
}

function setText(node, text) {
    node.textContent = text || '';
    node.hidden = !text;
}

// The user's picture, as on Jellyfin's own lists, or the person icon when there is none
function avatar(user) {
    if (!user.PrimaryImageTag) {
        return icon('listItemIcon person');
    }

    const image = element('div', 'listItemImage kidsAvatar');
    const url = ApiClient.getUrl('Users/' + user.Id + '/Images/Primary', { tag: user.PrimaryImageTag, height: 96 });
    image.style.backgroundImage = 'url("' + url + '")';
    image.setAttribute('aria-hidden', 'true');
    return image;
}

// Shared with kidsUser.js (the two pages are separate modules): wording, health, live TV state and the server clock

function minutes(seconds) {
    return Math.floor(seconds / 60);
}

function hasAllowance(status) {
    return status.AllowanceSeconds !== undefined && status.AllowanceSeconds !== null;
}

// "No daily limit" and 0 never read alike
function allowanceText(allowanceSeconds) {
    if (allowanceSeconds === undefined || allowanceSeconds === null) {
        return 'No daily limit';
    }

    return allowanceSeconds === 0 ? 'No screen time today' : minutes(allowanceSeconds) + ' min a day';
}

function usageText(status) {
    const used = minutes(status.UsedSeconds);
    if (!hasAllowance(status)) {
        return 'Today: ' + used + ' min · No daily limit';
    }

    if (status.AllowanceSeconds === 0 && !(status.BonusSeconds > 0)) {
        return 'Today: ' + used + ' min · No screen time today';
    }

    const bonus = status.BonusSeconds > 0 ? ' + ' + minutes(status.BonusSeconds) + ' min bonus' : '';
    const of = 'Today: ' + used + ' of ' + minutes(status.AllowanceSeconds) + ' min' + bonus;
    return status.TimeUp ? of + ' · time is up' : of + ' · ' + minutes(status.RemainingSeconds) + ' min left';
}

function userTab(userId, tab) {
    return '#/dashboard/users/' + userId + '/' + tab;
}

function editorLink(userId) {
    return '#/configurationpage?name=KidsUser&userId=' + encodeURIComponent(userId);
}

// What still leaves a kid unprotected in their Jellyfin policy, each with where to fix it (research R2). Null when
// the policy is unknown: never "no issue" then.
function healthIssues(user, pickedIds, serverChannels) {
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
        issues.push({ text: 'Live TV access is on but no channels are picked', href: editorLink(user.Id), linkText: 'Pick' });
    }

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

// Page state, refreshed on each viewshow
let dayStartHour = null;
let serverTimeShown = false;

function renderServerTime(view, status) {
    const clock = serverClock(status, dayStartHour);
    if (!clock || serverTimeShown) {
        return;
    }

    serverTimeShown = true;
    setText(view.querySelector('.kidsServerTime'), 'Server time: ' + clock.now + ' (' + clock.label + ').');
}

function renderHealth(body, user, pickedIds, serverChannels) {
    body.querySelectorAll('.kidsHealth').forEach((node) => node.remove());
    const issues = healthIssues(user, pickedIds, serverChannels);
    for (const issue of issues || []) {
        const line = element('div', 'listItemBodyText secondary kidsWarning kidsHealth');
        line.append(icon('warning'), issue.text);
        const fix = element('a', 'button-link', issue.linkText || 'Fix');
        fix.setAttribute('is', 'emby-linkbutton');
        fix.href = issue.href;
        fix.setAttribute('aria-label', (issue.linkText || 'Fix') + ': ' + issue.text + ', ' + user.Name);
        line.append(fix);
        body.append(line);
    }
}

function renderUser(view, user, isKid, serverChannels) {
    const isAdmin = Boolean(user.Policy && user.Policy.IsAdministrator);
    const item = element('div', 'listItem listItem-border kidsUserRow');
    item.dataset.action = 'none';
    const body = element('div', 'listItemBody');
    body.append(element('div', 'listItemBodyText kidsUserName', user.Name));

    // "Administrator", "Kid", "Not a kid"; a kid's daily allowance is added once their status is loaded
    const role = [];
    if (isAdmin) {
        role.push('Administrator');
    }

    if (isKid) {
        role.push('Kid');
    } else if (!isAdmin) {
        role.push('Not a kid');
    }

    const summary = element('div', 'listItemBodyText secondary kidsSummary', role.join(' · '));
    body.append(summary);

    if (isKid) {
        renderKid(view, user, body, summary, role, serverChannels);
    }

    const label = element('label', 'kidsKidCheckbox');
    const checkbox = element('input');
    checkbox.type = 'checkbox';
    checkbox.setAttribute('is', 'emby-checkbox');
    checkbox.checked = isKid;
    checkbox.setAttribute('aria-label', 'Kid: ' + user.Name);
    label.append(checkbox, element('span', null, 'Kid'));
    checkbox.addEventListener('change', () => onToggle(view, user, checkbox));

    item.append(avatar(user), body);

    // The Kid checkbox stays last, so it lines up on every row
    if (isKid) {
        const edit = element('a', 'emby-button button-flat kidsEdit');
        edit.setAttribute('is', 'emby-linkbutton');
        edit.href = editorLink(user.Id);
        edit.title = 'Edit the settings of ' + user.Name;
        edit.setAttribute('aria-label', 'Edit the settings of ' + user.Name);
        edit.append(icon('tune'), element('span', 'kidsEditLabel', 'Edit settings'));
        item.append(edit);
    }

    item.append(label);
    return item;
}

// A kid's lines: today's screen time from Status (with the "App only" pill), live TV and health from the profile
function renderKid(view, user, body, summary, role, serverChannels) {
    const usage = element('div', 'listItemBodyText secondary kidsUsage');
    usage.setAttribute('role', 'status');
    usage.textContent = 'Today: …';
    const liveTv = element('div', 'listItemBodyText secondary kidsLiveTv', 'Live TV: …');
    body.append(usage, liveTv);
    // Until the profile is read, health uses no channel list (the live TV lines wait for it)
    renderHealth(body, user, null, null);

    // Never show 0 when the status could not be read
    request('GET', 'Kids/Users/' + user.Id + '/Status')
        .then((status) => {
            summary.textContent = role.concat([allowanceText(status.AllowanceSeconds)]).join(' · ');
            usage.textContent = usageText(status);
            if (status.Enforced === false) {
                const pill = element('span', 'kidsPill kidsAppOnly', 'App only');
                pill.title = 'Limits only apply in Wholphin while enforcement is off';
                usage.append(pill);
            }

            renderServerTime(view, status);
        })
        .catch(() => {
            usage.textContent = 'Usage unavailable';
        });

    request('GET', 'Kids/Users/' + user.Id + '/Profile')
        .then((profile) => {
            const picked = profile.LiveTvChannelIds || [];
            liveTv.textContent = liveTvText(user, picked, serverChannels);
            renderHealth(body, user, picked, serverChannels);
        })
        .catch(() => {
            liveTv.textContent = 'Live TV unavailable';
        });
}

// The household banner: enforcement off, or a self-check that could not guard every route
function renderBanner(view, config, info) {
    const enforcementOff = Boolean(config) && !config.EnforcementEnabled;
    view.querySelector('.kidsBannerEnforcement').hidden = !enforcementOff;
    const gate = info && info.Enforcement;
    setText(view.querySelector('.kidsBannerGate'), gate && gate.Gate === 'degraded'
        ? 'Some playback routes can\'t be guarded on this Jellyfin version: ' + (gate.Missing || []).join(', ')
            + '. Kids may still play through them. Check for a plugin update.'
        : null);
    const liveTv = info && info.LiveTv;
    setText(view.querySelector('.kidsBannerLiveTv'), liveTv && liveTv.Gate === 'degraded'
        ? 'Some live TV routes can\'t be guarded on this Jellyfin version: ' + (liveTv.Missing || []).join(', ') + '.'
        : null);
    view.querySelector('.kidsBanner').hidden = [...view.querySelectorAll('.kidsBannerItem')].every((node) => node.hidden);
}

// Screen time settings ("day starts at", enforcement, the sitting break), saved in the plugin configuration through
// Jellyfin's own plugin configuration API

// The plugin id is looked up by name, so this anonymously served file holds no id (check j)
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

// Reads the configuration, changes it and writes it all back, so other settings (and other tabs' changes) are kept
function updateConfiguration(change) {
    return pluginId().then((id) => ApiClient.getPluginConfiguration(id).then((config) => {
        change(config);
        return ApiClient.updatePluginConfiguration(id, config);
    }));
}

// The server's ranges (PluginConfiguration, PluginKidsSettings): it reads values outside them as the default
const defaultSittingBreakMinutes = 15;

function sittingBreakIsValid(text) {
    const value = Number(text.trim());
    return text.trim() !== '' && Number.isInteger(value) && value >= 1 && value <= 240;
}

// Shows or clears the range message under the break field, and says whether the field is valid
function renderSittingBreak(view) {
    const input = view.querySelector('.kidsSittingBreak');
    // A number input reports bad input (such as "abc") as an empty value with validity.badInput
    const valid = !input.validity.badInput && sittingBreakIsValid(input.value);
    setText(view.querySelector('.kidsSittingBreakError'), valid ? null : 'Between 1 and 240 minutes.');
    input.setAttribute('aria-invalid', String(!valid));
    return valid;
}

// Unsaved changes in the screen time form: compared with what was loaded or last saved
let screenTimeSnapshot = null;

function screenTimeValues(view) {
    return JSON.stringify([
        view.querySelector('.kidsDayStart').value,
        view.querySelector('.kidsEnforcementEnabled').checked,
        view.querySelector('.kidsSittingBreak').value.trim()
    ]);
}

function isDirty(view) {
    return screenTimeSnapshot !== null && screenTimeValues(view) !== screenTimeSnapshot;
}

function renderDirty(view) {
    view.querySelector('.kidsScreenTimeUnsaved').hidden = !isDirty(view);
}

function takeSnapshot(view) {
    screenTimeSnapshot = screenTimeValues(view);
    renderDirty(view);
}

function applyScreenTime(view, config) {
    const hour = Number(config && config.DayStartsAtHour);
    dayStartHour = Number.isInteger(hour) && hour >= 0 && hour <= 23 ? hour : 0;
    view.querySelector('.kidsDayStart').value = String(dayStartHour);
    view.querySelector('.kidsEnforcementEnabled').checked = Boolean(config && config.EnforcementEnabled);
    // Show what the server applies
    const breakMinutes = String(config && config.SittingBreakMinutes);
    view.querySelector('.kidsSittingBreak').value = sittingBreakIsValid(breakMinutes) ? breakMinutes : String(defaultSittingBreakMinutes);
    renderSittingBreak(view);
    takeSnapshot(view);
}

function saveScreenTime(view) {
    if (!renderSittingBreak(view)) {
        return;
    }

    Dashboard.showLoadingMsg();
    updateConfiguration((config) => {
        config.DayStartsAtHour = Number(view.querySelector('.kidsDayStart').value);
        config.EnforcementEnabled = view.querySelector('.kidsEnforcementEnabled').checked;
        config.SittingBreakMinutes = Number(view.querySelector('.kidsSittingBreak').value.trim());
    })
        .then(() => {
            toast('Screen time settings saved');
            // Usage lines, the banner and the pills follow the new settings
            return refresh(view);
        })
        .catch(() => {
            Dashboard.hideLoadingMsg();
            Dashboard.alert({ title: 'Kids', message: 'Could not save the screen time settings.' });
        });
}

function turnOnEnforcement(view) {
    Dashboard.confirm({
        title: 'Turn on enforcement',
        text: 'The server will stop playback and refuse new playback on every app when a kid\'s limits run out.',
        confirmText: 'Turn on'
    }, '', (confirmed) => {
        if (!confirmed) {
            return;
        }

        Dashboard.showLoadingMsg();
        updateConfiguration((config) => {
            config.EnforcementEnabled = true;
        })
            .then(() => {
                toast('Enforcement is on');
                return refresh(view);
            })
            .catch(() => {
                Dashboard.hideLoadingMsg();
                Dashboard.alert({ title: 'Kids', message: 'Could not turn on enforcement.' });
            });
    });
}

// The enforcement self-check from GET Kids/Info: whether this Jellyfin version still has every playback route the
// plugin guards. It describes the server, not the household, so it is shown with enforcement on or off.
function renderGate(view, info) {
    const line = view.querySelector('.kidsGate');
    line.classList.remove('kidsProblem');
    const gate = info && info.Enforcement;
    if (info === null) {
        line.classList.add('kidsProblem');
        line.textContent = 'Playback routes: could not read the self-check.';
    } else if (!gate) {
        line.textContent = 'Playback routes: this plugin version does not report them.';
    } else if (gate.Gate === 'ok') {
        line.textContent = 'Playback routes: all guarded on this Jellyfin version.';
    } else if (gate.Gate === 'pending') {
        line.textContent = 'Playback routes: checking at startup. Reload the page in a moment.';
    } else {
        const missing = gate.Missing || [];
        line.classList.add('kidsProblem');
        line.textContent = 'Playback routes: ' + missing.length + ' cannot be guarded on this Jellyfin version, '
            + 'so kids may still play through them: ' + missing.join(', ') + '. Check for a plugin update.';
    }
}

function onToggle(view, user, checkbox) {
    if (checkbox.checked) {
        Dashboard.showLoadingMsg();
        request('PUT', 'Kids/Users/' + user.Id + '/Profile', { Revision: 0 })
            .then(() => {
                toast(user.Name + ' is now a kid');
                return load(view);
            })
            .catch((response) => {
                // Already a kid, or the user was deleted meanwhile: the list is out of date
                if (response && (response.status === 409 || response.status === 404)) {
                    return load(view);
                }

                checkbox.checked = false;
                Dashboard.hideLoadingMsg();
                Dashboard.alert({ title: 'Kids', message: 'Could not mark ' + escapeHtml(user.Name) + ' as a kid.' });
                return undefined;
            });
        return;
    }

    Dashboard.confirm({
        title: 'Remove kid settings',
        text: 'Remove the kid settings of ' + escapeHtml(user.Name) + '? This cannot be undone. Today\'s screen time is kept.',
        confirmText: 'Remove',
        primary: 'delete'
    }, '', (confirmed) => {
        if (!confirmed) {
            checkbox.checked = true;
            return;
        }

        Dashboard.showLoadingMsg();
        request('DELETE', 'Kids/Users/' + user.Id + '/Profile')
            .then(() => {
                toast(user.Name + ' is no longer a kid');
                return load(view);
            })
            .catch((response) => {
                if (response && response.status === 404) {
                    return load(view);
                }

                checkbox.checked = true;
                Dashboard.hideLoadingMsg();
                Dashboard.alert({ title: 'Kids', message: 'Could not unmark ' + escapeHtml(user.Name) + '.' });
                return undefined;
            });
    });
}

function load(view) {
    Dashboard.showLoadingMsg();
    showError(view, null);
    serverTimeShown = false;
    setText(view.querySelector('.kidsServerTime'), null);
    const list = view.querySelector('.kidsUsers');
    // The channel list only feeds the live TV lines: without it they say less
    const channels = request('GET', 'Kids/Admin/LiveTvChannels').catch(() => null);
    return Promise.all([ApiClient.getUsers(), request('GET', 'Kids/Profiles'), channels])
        .then(([users, kids, serverChannels]) => {
            const kidIds = new Set(kids.map((kid) => kid.UserId));
            const sorted = [...users].sort((a, b) => a.Name.localeCompare(b.Name));
            list.replaceChildren(...sorted.map((user) => renderUser(view, user, kidIds.has(user.Id), serverChannels)));
        })
        .catch(() => {
            // Never show an empty list when the kids could not be loaded: it would read as "no kids"
            list.replaceChildren();
            showError(view, 'Could not load the kids. Check the server log.');
        })
        .finally(() => Dashboard.hideLoadingMsg());
}

// Everything that depends on the household settings: the banner, the screen time form, the self-check and the list
function refresh(view) {
    const config = pluginId().then((id) => ApiClient.getPluginConfiguration(id)).catch(() => null);
    const info = request('GET', 'Kids/Info').catch(() => null);
    return Promise.all([config, info]).then(([loaded, kidsInfo]) => {
        if (loaded) {
            applyScreenTime(view, loaded);
        } else {
            Dashboard.alert({ title: 'Kids', message: 'Could not load the screen time settings.' });
        }

        renderBanner(view, loaded, kidsInfo);
        renderGate(view, kidsInfo);
        return load(view);
    });
}

// Parent lock section. The code is never shown again after saving, and the page never asks for the verifier.

const lockTypeNames = { NUMERIC: 'Digits', REMOTE: 'Remote buttons' };
const lockSymbols = { NUMERIC: '0123456789', REMOTE: 'UDLR0123456789rgybpwf+-i' };

// A message under the lock form: a problem in the theme's error colour
function setLockMessage(view, text, isProblem) {
    const message = view.querySelector('.kidsLockMessage');
    message.classList.toggle('kidsProblem', Boolean(isProblem));
    setText(message, text);
}

// Mirrors the server's rules (ParentCode): 4 to 32 symbols from the type's set, compared exactly
function codeProblem(lockType, code, confirmation) {
    if (code.length < 4 || code.length > 32) {
        return 'The code must be 4 to 32 symbols.';
    }

    if ([...code].some((symbol) => !lockSymbols[lockType].includes(symbol))) {
        return lockType === 'NUMERIC' ? 'Use digits only.' : 'Use only U D L R, 0–9 and r g y b p w f + - i.';
    }

    return code === confirmation ? null : 'The two codes are different.';
}

function renderLock(view, status) {
    const hasLock = Boolean(status && status.LockType);
    view.querySelector('.kidsLockStatus').textContent = hasLock
        ? 'Parent lock on: ' + (lockTypeNames[status.LockType] || status.LockType)
        : 'No parent lock is set';
    setText(view.querySelector('.kidsLockChanged'), hasLock && status.ChangedAt
        ? 'Set ' + new Date(status.ChangedAt).toLocaleString()
        : null);
    const lockIcon = view.querySelector('.kidsLockIcon');
    lockIcon.classList.toggle('lock', hasLock);
    lockIcon.classList.toggle('lock_open', !hasLock);
    view.querySelector('.kidsLockAdvice').hidden = hasLock;
    // emby-button styles override the hidden attribute, so hide the button through its style
    view.querySelector('.kidsLockRemove').style.display = hasLock ? '' : 'none';
    view.querySelector('.kidsLockSave span').textContent = hasLock ? 'Change code' : 'Save code';
    if (hasLock) {
        view.querySelector('.kidsLockType').value = status.LockType;
    }

    updateRemoteHelp(view);
}

function updateRemoteHelp(view) {
    view.querySelector('.kidsLockRemoteHelp').hidden = view.querySelector('.kidsLockType').value !== 'REMOTE';
}

function loadLock(view) {
    setText(view.querySelector('.kidsLockError'), null);
    return request('GET', 'Kids/Parent/Lock')
        .then((status) => renderLock(view, status))
        .catch(() => {
            // Never read a failure as "no lock"
            view.querySelector('.kidsLockStatus').textContent = '';
            setText(view.querySelector('.kidsLockChanged'), null);
            setText(view.querySelector('.kidsLockError'), 'Could not load the parent lock. Check the server log.');
        });
}

function saveLock(view) {
    const lockType = view.querySelector('.kidsLockType').value;
    const codeInput = view.querySelector('.kidsLockCode');
    const confirmInput = view.querySelector('.kidsLockConfirm');
    const problem = codeProblem(lockType, codeInput.value, confirmInput.value);
    if (problem) {
        setLockMessage(view, problem, true);
        return;
    }

    Dashboard.showLoadingMsg();
    request('PUT', 'Kids/Parent/Lock', { LockType: lockType, Code: codeInput.value })
        .then((status) => {
            codeInput.value = '';
            confirmInput.value = '';
            renderLock(view, status);
            setLockMessage(view, null);
            toast('Parent lock saved');
        })
        .catch((response) => {
            if (response && response.status === 400 && typeof response.json === 'function') {
                return response.json().then((problemDetails) => {
                    const errors = (problemDetails && problemDetails.errors) || {};
                    const first = errors.Code || errors.LockType;
                    setLockMessage(view, first ? first[0] : 'The code was refused.', true);
                }, () => setLockMessage(view, 'The code was refused.', true));
            }

            setLockMessage(view, null);
            Dashboard.alert({ title: 'Kids', message: 'Could not save the parent lock.' });
            return undefined;
        })
        .finally(() => Dashboard.hideLoadingMsg());
}

function removeLock(view) {
    Dashboard.confirm({
        title: 'Remove the parent lock',
        text: 'Remove the parent lock? TVs will need an administrator sign-in to change kids settings.',
        confirmText: 'Remove',
        primary: 'delete'
    }, '', (confirmed) => {
        if (!confirmed) {
            return;
        }

        Dashboard.showLoadingMsg();
        request('DELETE', 'Kids/Parent/Lock')
            .then(() => {
                renderLock(view, null);
                toast('Parent lock removed');
            })
            .catch((response) => {
                if (response && response.status === 404) {
                    renderLock(view, null);
                    return;
                }

                Dashboard.alert({ title: 'Kids', message: 'Could not remove the parent lock.' });
            })
            .finally(() => {
                setLockMessage(view, null);
                Dashboard.hideLoadingMsg();
            });
    });
}

export default function (view) {
    // Reloading or closing the tab with unsaved settings asks first; Jellyfin's own navigation cannot be held back
    const onBeforeUnload = (event) => {
        if (isDirty(view)) {
            event.preventDefault();
            event.returnValue = '';
        }
    };

    view.querySelector('.kidsLockForm').addEventListener('submit', (event) => {
        event.preventDefault();
        saveLock(view);
    });
    view.querySelector('.kidsLockRemove').addEventListener('click', () => removeLock(view));
    view.querySelector('.kidsLockType').addEventListener('change', () => updateRemoteHelp(view));
    const screenTime = view.querySelector('.kidsScreenTimeForm');
    screenTime.addEventListener('submit', (event) => {
        event.preventDefault();
        saveScreenTime(view);
    });
    screenTime.addEventListener('input', () => renderDirty(view));
    screenTime.addEventListener('change', () => renderDirty(view));
    view.querySelector('.kidsSittingBreak').addEventListener('input', () => renderSittingBreak(view));
    view.querySelector('.kidsTurnOn').addEventListener('click', () => turnOnEnforcement(view));
    view.addEventListener('viewshow', () => {
        window.addEventListener('beforeunload', onBeforeUnload);
        loadLock(view);
        refresh(view);
    });
    view.addEventListener('viewhide', () => window.removeEventListener('beforeunload', onBeforeUnload));
}
