// The Kids dashboard page: lists Jellyfin users, marks and unmarks kids, warns about kids who are administrators.
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

export default function (view) {
    view.addEventListener('viewshow', () => load(view));
}
