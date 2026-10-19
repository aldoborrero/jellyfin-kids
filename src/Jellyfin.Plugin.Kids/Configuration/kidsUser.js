// The kid settings editor: the daily screen-time allowance, the session limit and today's bonus minutes; search,
// autoplay and its limit; home sections (order and visibility); and the picked collections and playlists. Saves with
// the loaded revision; a conflict never overwrites. Bonus minutes are added straight away, apart from the save.

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

function iconButton(icon, label, onClick, disabled) {
    const button = element('button', 'listItemButton paper-icon-button-light');
    button.type = 'button';
    button.setAttribute('is', 'paper-icon-button-light');
    button.title = label;
    button.setAttribute('aria-label', label);
    button.disabled = Boolean(disabled);
    button.append(element('span', 'material-icons ' + icon));
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
    const icon = element('span', 'material-icons listItemIcon ' + (type === 'Playlist' ? 'playlist_play' : 'video_library'));
    icon.setAttribute('aria-hidden', 'true');
    return icon;
}

// A list row that is not itself clickable: its controls are
function row(className) {
    const item = element('div', 'listItem kidsRow' + (className ? ' ' + className : ''));
    item.dataset.action = 'none';
    return item;
}

// Today's screen time, from GET Status or the answer to a bonus

function minutes(seconds) {
    return Math.floor(seconds / 60);
}

function usageText(status) {
    const used = minutes(status.UsedSeconds);
    if (status.AllowanceSeconds === undefined || status.AllowanceSeconds === null) {
        return 'Today: ' + used + ' min (no daily limit)';
    }

    const bonus = status.BonusSeconds > 0 ? ' + ' + minutes(status.BonusSeconds) + ' min bonus' : '';
    const of = 'Today: ' + used + ' of ' + minutes(status.AllowanceSeconds) + ' min' + bonus;
    return status.TimeUp ? of + ' · time is up' : of + ' · ' + minutes(status.RemainingSeconds) + ' min left';
}

export default function (view, params) {
    const userId = params.userId;
    const form = view.querySelector('.kidsUserForm');
    const capInput = view.querySelector('.kidsAutoplayCap');
    const autoplayInput = view.querySelector('.kidsAutoplayEnabled');
    const searchInput = view.querySelector('.kidsPickSearch');
    const results = view.querySelector('.kidsPickResults');

    // Editor state, filled from the server profile
    let revision = 0;
    let sections = [];
    let picks = []; // { Id, Name, Type, missing }
    let conflictProfile = null;
    let searchTimer = null;

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

    // A whole-minutes field where empty means no limit: value() is null when empty, the minutes when in range, or
    // undefined when invalid; render() shows or clears the range message next to it and says whether it is valid
    function minutesField(selector, field, min, max) {
        const input = view.querySelector(selector);
        const problem = 'Between ' + min + ' and ' + max + ' minutes, or empty for no limit.';
        const value = () => {
            const text = input.value.trim();
            if (text === '') {
                return null;
            }

            const minutes = Number(text);
            return Number.isInteger(minutes) && minutes >= min && minutes <= max ? minutes : undefined;
        };
        const render = () => {
            // A number input reports bad input (such as "abc") as an empty value with validity.badInput
            const invalid = input.validity.badInput || value() === undefined;
            view.querySelector('.kidsError[data-error-for="' + field + '"]').textContent = invalid ? problem : '';
            return !invalid;
        };
        const load = (minutes) => {
            input.value = minutes === undefined || minutes === null ? '' : String(minutes);
        };
        input.addEventListener('input', render);
        return { value, render, load };
    }

    const allowance = minutesField('.kidsDailyAllowance', 'DailyAllowanceMinutes', 0, 1440);
    const sessionLimit = minutesField('.kidsSessionLimit', 'SessionLimitMinutes', 5, 120);

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
            });
            label.append(checkbox, element('span', null, sectionNames[entry.Section] || entry.Section));
            item.append(
                label,
                iconButton('arrow_upward', 'Move up', () => { move(sections, index, -1); renderSections(); }, index === 0),
                iconButton('arrow_downward', 'Move down', () => { move(sections, index, 1); renderSections(); }, index === sections.length - 1));
            return item;
        }));
    }

    function renderPicks() {
        const list = view.querySelector('.kidsPicks');
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
        sections = profile.Sections.map((entry) => ({ Section: entry.Section, Visible: entry.Visible }));
        picks = profile.PickedItemIds.map((id) => ({ Id: id, Name: '', Type: null, missing: true }));
        clearErrors();
        view.querySelector('.kidsConflict').hidden = true;
        renderCap();
        renderSections();
        renderPicks();

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

    function setBonusError(text) {
        bonusError.textContent = text || '';
        bonusError.hidden = !text;
    }

    function loadUsage() {
        usageLine.textContent = 'Today: …';
        // Never show 0 when the status could not be read
        request('GET', 'Kids/Users/' + userId + '/Status')
            .then((status) => {
                usageLine.textContent = usageText(status);
            })
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
                usageLine.textContent = usageText(status);
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

    function load() {
        Dashboard.showLoadingMsg();
        showMessage(null);
        form.hidden = true;
        return Promise.all([request('GET', 'Kids/Users/' + userId + '/Profile'), ApiClient.getUser(userId)])
            .then(([profile, user]) => {
                view.querySelector('.kidsUserTitle').textContent = 'Kid settings: ' + user.Name;
                return applyProfile(profile).then(() => {
                    form.hidden = false;
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

        const body = {
            Revision: revision,
            SearchAllowed: view.querySelector('.kidsSearchAllowed').checked,
            AutoplayEnabled: autoplayInput.checked,
            AutoplayCap: Number(capInput.value),
            Sections: sections.map((entry) => ({ Section: entry.Section, Visible: entry.Visible })),
            PickedItemIds: picks.map((pick) => pick.Id),
            // Always sent: the update is a full replacement, and omitting them would clear them
            DailyAllowanceMinutes: allowance.value(),
            SessionLimitMinutes: sessionLimit.value()
        };

        Dashboard.showLoadingMsg();
        request('PUT', 'Kids/Users/' + userId + '/Profile', body)
            .then((profile) => {
                revision = profile.Revision;
                Dashboard.alert('Saved');
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

    form.addEventListener('submit', save);
    bonusAdd.addEventListener('click', addBonus);
    bonusInput.addEventListener('keydown', (event) => {
        // Enter adds the bonus instead of saving the whole form
        if (event.key === 'Enter') {
            event.preventDefault();
            addBonus();
        }
    });
    autoplayInput.addEventListener('change', renderCap);
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
    view.addEventListener('viewshow', load);
}
