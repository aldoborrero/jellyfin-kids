// The Kids dashboard page: the household parent lock, and the list of Jellyfin users where kids are marked and
// unmarked, with a warning for kids who are administrators.
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

function showError(view, message) {
    const error = view.querySelector('.kidsError');
    error.textContent = message || '';
    error.style.display = message ? '' : 'none';
}

function renderUser(view, user, isKid) {
    const isAdmin = Boolean(user.Policy && user.Policy.IsAdministrator);
    const item = element('div', 'listItem listItem-border');
    const body = element('div', 'listItemBody two-line');
    body.style.flexGrow = '1';

    const title = element('div', 'listItemBodyText', user.Name);
    if (isAdmin) {
        title.append(' ', element('span', 'fieldDescription', '(Administrator)'));
    }

    body.append(title);

    if (isKid && isAdmin) {
        const warning = element('div', 'listItemBodyText secondary kidsAdminWarning', '⚠ ' + adminWarning);
        warning.style.color = '#ff9800';
        body.append(warning);
    }

    if (isKid) {
        const link = element('a', 'button-link', 'Edit settings');
        link.href = '#/configurationpage?name=KidsUser&userId=' + encodeURIComponent(user.Id);
        const linkLine = element('div', 'listItemBodyText secondary');
        linkLine.append(link);
        body.append(linkLine);
    }

    const label = element('label', 'checkboxContainer');
    const checkbox = element('input');
    checkbox.type = 'checkbox';
    checkbox.setAttribute('is', 'emby-checkbox');
    checkbox.checked = isKid;
    checkbox.setAttribute('aria-label', 'Kid: ' + user.Name);
    label.append(checkbox, element('span', null, 'Kid'));
    checkbox.addEventListener('change', () => onToggle(view, user, checkbox));

    item.append(body, label);
    return item;
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

    Dashboard.confirm('Remove the kid settings of ' + user.Name + '? This cannot be undone.', 'Kids', (confirmed) => {
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
    const statusLine = view.querySelector('.kidsLockStatus');
    statusLine.textContent = hasLock
        ? 'Parent lock: ' + (lockTypeNames[status.LockType] || status.LockType)
            + (status.ChangedAt ? ', last changed ' + new Date(status.ChangedAt).toLocaleString() : '')
        : 'No parent lock is set.';
    view.querySelector('.kidsLockAdvice').hidden = hasLock;
    // emby-button styles override the hidden attribute, so hide the button through its style
    view.querySelector('.kidsLockRemove').style.display = hasLock ? '' : 'none';
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
            setText(view.querySelector('.kidsLockError'), 'Could not load the parent lock. Check the server log.');
        });
}

function saveLock(view) {
    const lockType = view.querySelector('.kidsLockType').value;
    const codeInput = view.querySelector('.kidsLockCode');
    const confirmInput = view.querySelector('.kidsLockConfirm');
    const message = view.querySelector('.kidsLockMessage');
    const problem = codeProblem(lockType, codeInput.value, confirmInput.value);
    if (problem) {
        message.style.color = '#ff6b6b';
        setText(message, problem);
        return;
    }

    Dashboard.showLoadingMsg();
    request('PUT', 'Kids/Parent/Lock', { LockType: lockType, Code: codeInput.value })
        .then((status) => {
            codeInput.value = '';
            confirmInput.value = '';
            renderLock(view, status);
            message.style.color = '';
            setText(message, 'Saved.');
        })
        .catch((response) => {
            message.style.color = '#ff6b6b';
            if (response && response.status === 400 && typeof response.json === 'function') {
                return response.json().then((problemDetails) => {
                    const errors = (problemDetails && problemDetails.errors) || {};
                    const first = errors.Code || errors.LockType;
                    setText(message, first ? first[0] : 'The code was refused.');
                }, () => setText(message, 'The code was refused.'));
            }

            setText(message, null);
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
                setText(view.querySelector('.kidsLockMessage'), null);
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
    view.addEventListener('viewshow', () => {
        loadLock(view);
        load(view);
    });
}
