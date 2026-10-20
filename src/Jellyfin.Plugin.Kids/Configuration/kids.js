// The Kids dashboard page: the list of Jellyfin users where kids are marked and unmarked, with today's screen time and
// a warning for kids who are administrators; the household parent lock; and the screen time settings: "day starts at",
// enforcement on every app with its self-check, and the break between sittings.
// Data comes only from authenticated calls; the page HTML is served anonymously and holds none.

const adminWarning = 'Administrators bypass every restriction. Use a non-admin account for this kid.';

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

function showError(view, message) {
    const error = view.querySelector('.kidsError');
    error.textContent = message || '';
    error.style.display = message ? '' : 'none';
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

function renderUser(view, user, isKid) {
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

    if (isKid && isAdmin) {
        const warning = element('div', 'listItemBodyText secondary kidsWarning kidsAdminWarning');
        warning.append(icon('warning'), adminWarning);
        body.append(warning);
    }

    if (isKid) {
        body.append(renderUsage(user, summary, role));
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
        edit.href = '#/configurationpage?name=KidsUser&userId=' + encodeURIComponent(user.Id);
        edit.title = 'Edit the settings of ' + user.Name;
        edit.setAttribute('aria-label', 'Edit the settings of ' + user.Name);
        edit.append(icon('tune'), element('span', 'kidsEditLabel', 'Edit settings'));
        item.append(edit);
    }

    item.append(label);
    return item;
}

// Today's screen time per kid, from GET Status; bonus minutes are given in the kid's editor

function minutes(seconds) {
    return Math.floor(seconds / 60);
}

function hasAllowance(status) {
    return status.AllowanceSeconds !== undefined && status.AllowanceSeconds !== null;
}

function usageText(status) {
    const used = minutes(status.UsedSeconds);
    if (!hasAllowance(status)) {
        return 'Today: ' + used + ' min';
    }

    const bonus = status.BonusSeconds > 0 ? ' + ' + minutes(status.BonusSeconds) + ' min bonus' : '';
    const of = 'Today: ' + used + ' of ' + minutes(status.AllowanceSeconds) + ' min' + bonus;
    return status.TimeUp ? of + ' · time is up' : of + ' · ' + minutes(status.RemainingSeconds) + ' min left';
}

function renderUsage(user, summary, role) {
    const line = element('div', 'listItemBodyText secondary kidsUsage', 'Today: …');

    // Never show 0 when the status could not be read
    request('GET', 'Kids/Users/' + user.Id + '/Status')
        .then((status) => {
            const allowance = hasAllowance(status) ? minutes(status.AllowanceSeconds) + ' min a day' : 'no daily limit';
            summary.textContent = role.concat([allowance]).join(' · ');
            line.textContent = usageText(status);
        })
        .catch(() => {
            line.textContent = 'Usage unavailable';
        });

    return line;
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
    return valid;
}

function loadScreenTime(view) {
    return pluginId().then((id) => ApiClient.getPluginConfiguration(id)).then((config) => {
        const hour = Number(config && config.DayStartsAtHour);
        view.querySelector('.kidsDayStart').value = String(Number.isInteger(hour) && hour >= 0 && hour <= 23 ? hour : 0);
        view.querySelector('.kidsEnforcementEnabled').checked = Boolean(config && config.EnforcementEnabled);
        // Show what the server applies
        const breakMinutes = String(config && config.SittingBreakMinutes);
        view.querySelector('.kidsSittingBreak').value = sittingBreakIsValid(breakMinutes) ? breakMinutes : String(defaultSittingBreakMinutes);
        renderSittingBreak(view);
    });
}

function saveScreenTime(view) {
    if (!renderSittingBreak(view)) {
        return;
    }

    Dashboard.showLoadingMsg();
    // Read, change, write, so other settings are kept
    pluginId()
        .then((id) => ApiClient.getPluginConfiguration(id).then((config) => {
            config.DayStartsAtHour = Number(view.querySelector('.kidsDayStart').value);
            config.EnforcementEnabled = view.querySelector('.kidsEnforcementEnabled').checked;
            config.SittingBreakMinutes = Number(view.querySelector('.kidsSittingBreak').value.trim());
            return ApiClient.updatePluginConfiguration(id, config);
        }))
        .then((result) => {
            Dashboard.processPluginConfigurationUpdateResult(result);
            // Usage lines follow the new day
            return load(view);
        })
        .catch(() => {
            Dashboard.hideLoadingMsg();
            Dashboard.alert({ title: 'Kids', message: 'Could not save the screen time settings.' });
        });
}

// The enforcement self-check from GET Kids/Info: whether this Jellyfin version still has every playback route the
// plugin guards. It describes the server, not the household, so it is shown with enforcement on or off.
function loadGate(view) {
    const line = view.querySelector('.kidsGate');
    line.classList.remove('kidsProblem');
    line.textContent = 'Playback routes: checking…';
    return request('GET', 'Kids/Info')
        .then((info) => {
            const gate = info && info.Enforcement;
            if (!gate) {
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
        })
        .catch(() => {
            line.classList.add('kidsProblem');
            line.textContent = 'Playback routes: could not read the self-check.';
        });
}

function onToggle(view, user, checkbox) {
    if (checkbox.checked) {
        Dashboard.showLoadingMsg();
        request('PUT', 'Kids/Users/' + user.Id + '/Profile', { Revision: 0 })
            .then(() => load(view))
            .catch((response) => {
                // Already a kid, or the user was deleted meanwhile: the list is out of date
                if (response && (response.status === 409 || response.status === 404)) {
                    return load(view);
                }

                checkbox.checked = false;
                Dashboard.hideLoadingMsg();
                Dashboard.alert({ title: 'Kids', message: 'Could not mark ' + user.Name + ' as a kid.' });
            });
        return;
    }

    Dashboard.confirm('Remove the kid settings of ' + user.Name + '? This cannot be undone. Today\'s screen time is kept.', 'Kids', (confirmed) => {
        if (!confirmed) {
            checkbox.checked = true;
            return;
        }

        Dashboard.showLoadingMsg();
        request('DELETE', 'Kids/Users/' + user.Id + '/Profile')
            .then(() => load(view))
            .catch((response) => {
                if (response && response.status === 404) {
                    return load(view);
                }

                checkbox.checked = true;
                Dashboard.hideLoadingMsg();
                Dashboard.alert({ title: 'Kids', message: 'Could not unmark ' + user.Name + '.' });
            });
    });
}

function load(view) {
    Dashboard.showLoadingMsg();
    showError(view, null);
    const list = view.querySelector('.kidsUsers');
    return Promise.all([ApiClient.getUsers(), request('GET', 'Kids/Profiles')])
        .then(([users, kids]) => {
            const kidIds = new Set(kids.map((kid) => kid.UserId));
            const sorted = [...users].sort((a, b) => a.Name.localeCompare(b.Name));
            list.replaceChildren(...sorted.map((user) => renderUser(view, user, kidIds.has(user.Id))));
        })
        .catch(() => {
            // Never show an empty list when the kids could not be loaded: it would read as "no kids"
            list.replaceChildren();
            showError(view, 'Could not load the kids. Check the server log.');
        })
        .finally(() => Dashboard.hideLoadingMsg());
}

// Parent lock section. The code is never shown again after saving, and the page never asks for the verifier.

const lockTypeNames = { NUMERIC: 'Digits', REMOTE: 'Remote buttons' };
const lockSymbols = { NUMERIC: '0123456789', REMOTE: 'UDLR0123456789rgybpwf+-i' };

function setText(node, text) {
    node.textContent = text || '';
    node.hidden = !text;
}

// A message under the lock form: a problem in the theme's error colour, or a plain confirmation
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
            setLockMessage(view, 'Saved.', false);
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
    Dashboard.confirm('Remove the parent lock? TVs will need an administrator sign-in to change kids settings.', 'Kids', (confirmed) => {
        if (!confirmed) {
            return;
        }

        Dashboard.showLoadingMsg();
        request('DELETE', 'Kids/Parent/Lock')
            .then(() => renderLock(view, null))
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
    view.querySelector('.kidsLockForm').addEventListener('submit', (event) => {
        event.preventDefault();
        saveLock(view);
    });
    view.querySelector('.kidsLockRemove').addEventListener('click', () => removeLock(view));
    view.querySelector('.kidsLockType').addEventListener('change', () => updateRemoteHelp(view));
    view.querySelector('.kidsScreenTimeForm').addEventListener('submit', (event) => {
        event.preventDefault();
        saveScreenTime(view);
    });
    view.querySelector('.kidsSittingBreak').addEventListener('input', () => renderSittingBreak(view));
    view.addEventListener('viewshow', () => {
        load(view);
        loadLock(view);
        loadGate(view);
        loadScreenTime(view).catch(() => Dashboard.alert({ title: 'Kids', message: 'Could not load the screen time settings.' }));
    });
}
