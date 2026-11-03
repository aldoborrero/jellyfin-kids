# Kid editor with Jellyfin tabs Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Configure a kid from one page: the kid editor gains Profile, Access and Parental control tabs that edit the
kid's Jellyfin policy, next to today's settings (the Kids tab).

**Architecture:** Front end only, in `src/Jellyfin.Plugin.Kids/Configuration/kidsUser.html` and `kidsUser.js` (plus
the Fix links in `kids.js`). Each Jellyfin tab is its own form and saves like jellyfin-web's user tabs: re-read the
user, change only that tab's policy fields, `ApiClient.updateUserPolicy`. No server change. Design:
[2026-11-03-kid-editor-jellyfin-tabs-design.md](2026-11-03-kid-editor-jellyfin-tabs-design.md).

**Tech Stack:** Plain ES module for Jellyfin's dashboard (`ApiClient`, `Dashboard`, `emby-*` elements), Jellyfin
theme variables for colours. No JavaScript test framework: each task checks syntax with `node --check` and behaviour on
the dev server (`dev`, admin `admin`/`admin`, kids `alice` and `bob`).

**Conventions to keep:** every name goes in as text (`element()`, `textContent`), never HTML; colours only from
Jellyfin's theme variables; comments explain why, in the files' existing voice; commits are signed and carry no AI
attribution.

---

### Task 1: Fix links name a tab of the editor

Today each health issue carries an `href` to a Jellyfin user tab. In the editor it should name the editor tab
instead; on the Kids list page it should open the editor on that tab.

**Files:**
- Modify: `src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js:177-253` (`userTab`, `hiddenChannelIssues`, `healthIssues`)
- Modify: `src/Jellyfin.Plugin.Kids/Configuration/kids.js:135-212` (`userTab`, `editorLink`, `hiddenChannelIssues`, `healthIssues`)

**Step 1: kidsUser.js, issues carry a tab**

Delete `userTab` (lines 177-179). In `hiddenChannelIssues` return `{ text, tab: 'parental' }`. In `healthIssues`
replace each `href: userTab(user.Id, X)` with `tab: T`, where `profile` → `'profile'`, `access` → `'access'`,
`parentalcontrol` → `'parental'`; the "no channels are picked" issue gets `tab: 'kids'`. Update the comment above
`healthIssues` to: `each with the editor tab that fixes it`.

**Step 2: kids.js, links open the editor on that tab**

Replace `editorLink`:

```js
// The kid editor, optionally on one of its tabs (kids, profile, access, parental)
function editorLink(userId, tab) {
    return '#/configurationpage?name=KidsUser&userId=' + encodeURIComponent(userId) + (tab ? '&tab=' + tab : '');
}
```

Delete `userTab`, and replace each `href: userTab(user.Id, X)` with `href: editorLink(user.Id, T)` (same mapping as
step 1). The "Pick" issue becomes `href: editorLink(user.Id, 'kids')`.

**Step 3: Check syntax and leftovers**

Run: `node --check src/Jellyfin.Plugin.Kids/Configuration/kids.js && node --check src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js && rg -n userTab src/Jellyfin.Plugin.Kids/Configuration`
Expected: no output from `node`, and `rg` finds `userTab` only in `kidsUser.js` at `renderLiveTvWarnings` and
`renderSchedule` (removed in Task 2). `renderHealth` in `kidsUser.js` still reads `issue.href`; Task 2 changes it.

**Step 4: Commit**

```bash
git add src/Jellyfin.Plugin.Kids/Configuration/kids.js src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js
git commit -m "Point health issues at the kid editor's tabs"
```

---

### Task 2: Tabs in the kid editor

**Files:**
- Modify: `src/Jellyfin.Plugin.Kids/Configuration/kidsUser.html` (styles, lines 80-250)
- Modify: `src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js` (`renderLiveTvWarnings`, `renderHealth`, `renderSchedule`, `isDirty`, `load`, `save`, the listeners)

**Step 1: Markup**

In `kidsUser.html`:

1. Add to the `<style>` block:

```css
            #KidsUserPage .kidsEditor[hidden], #KidsUserPage .kidsPanel[hidden] { display: none; }
            #KidsUserPage .kidsTabs { display: flex; flex-wrap: wrap; gap: 0.25em; margin: 0 0 1.5em; border-bottom: 1px solid var(--jf-palette-divider, rgba(128, 128, 128, 0.3)); }
            #KidsUserPage .kidsTab { background: none; border: 0; border-bottom: 3px solid transparent; color: inherit; opacity: 0.75; font: inherit; padding: 0.75em 1em; min-height: 44px; cursor: pointer; }
            #KidsUserPage .kidsTab[aria-selected="true"] { opacity: 1; font-weight: 600; border-bottom-color: var(--jf-palette-primary-main, #00a4dc); }
            #KidsUserPage .kidsTab:focus-visible { outline: 2px solid var(--jf-palette-primary-main, #00a4dc); outline-offset: -2px; }
            #KidsUserPage .kidsTabUnsaved { margin-left: 0.35em; color: var(--jf-palette-warning-main, #f0ad4e); }
            #KidsUserPage .kidsTabUnsaved[hidden] { display: none; }
            #KidsUserPage .kidsAccountNote { margin: 0 0 1.5em; }
```

2. Wrap everything from `<form class="kidsUserForm" hidden>` to its `</form>` in
   `<div class="kidsEditor" hidden> … </div>`, and drop `hidden` from the form itself.
3. Move `<div class="kidsHealthList" role="status" aria-live="polite"></div>` out of the form, to the top of
   `.kidsEditor`, then add the tab strip under it:

```html
                    <div class="kidsTabs" role="tablist" aria-label="Kid settings">
                        <button type="button" role="tab" class="kidsTab" id="kidsTab-kids" data-tab="kids" aria-controls="kidsPanel-kids">Kids<span class="kidsTabUnsaved" hidden aria-label="unsaved changes">•</span></button>
                        <button type="button" role="tab" class="kidsTab" id="kidsTab-profile" data-tab="profile" aria-controls="kidsPanel-profile">Profile<span class="kidsTabUnsaved" hidden aria-label="unsaved changes">•</span></button>
                        <button type="button" role="tab" class="kidsTab" id="kidsTab-access" data-tab="access" aria-controls="kidsPanel-access">Access<span class="kidsTabUnsaved" hidden aria-label="unsaved changes">•</span></button>
                        <button type="button" role="tab" class="kidsTab" id="kidsTab-parental" data-tab="parental" aria-controls="kidsPanel-parental">Parental control<span class="kidsTabUnsaved" hidden aria-label="unsaved changes">•</span></button>
                    </div>
```

4. Wrap the form in `<div class="kidsPanel" id="kidsPanel-kids" data-tab="kids" role="tabpanel" aria-labelledby="kidsTab-kids"> … </div>`.
   After it add three empty panels, filled in Tasks 4 to 7:

```html
                    <div class="kidsPanel" id="kidsPanel-profile" data-tab="profile" role="tabpanel" aria-labelledby="kidsTab-profile" hidden></div>
                    <div class="kidsPanel" id="kidsPanel-access" data-tab="access" role="tabpanel" aria-labelledby="kidsTab-access" hidden></div>
                    <div class="kidsPanel" id="kidsPanel-parental" data-tab="parental" role="tabpanel" aria-labelledby="kidsTab-parental" hidden></div>
```

5. Delete the read-only "Allowed hours" section (`<div class="verticalSection …">` holding `.kidsSchedule` and
   `.kidsScheduleLink`); it moves to Parental control in Task 7.
6. In the "Live TV access is off" notice, replace the profile link with a tab switch:
   `Turn on <em>Allow Live TV access</em> on the <a is="emby-linkbutton" class="button-link kidsGoProfile" href="#">Profile tab</a>.`

**Step 2: JavaScript**

In `kidsUser.js`, inside the default export:

1. Under `const form = …` add `const editor = view.querySelector('.kidsEditor');` and the tab switch:

```js
    // Kids is this plugin's own settings; Profile, Access and Parental control edit the kid's Jellyfin policy
    const tabNames = ['kids', 'profile', 'access', 'parental'];

    function selectTab(name) {
        const tab = tabNames.includes(name) ? name : 'kids';
        view.querySelectorAll('.kidsTab').forEach((button) => {
            button.setAttribute('aria-selected', String(button.dataset.tab === tab));
            button.tabIndex = button.dataset.tab === tab ? 0 : -1;
        });
        view.querySelectorAll('.kidsPanel').forEach((panel) => {
            panel.hidden = panel.dataset.tab !== tab;
        });
    }
```

2. Replace every `form.hidden` with `editor.hidden` (`isDirty`, `load` twice, the 404 branch of `save`).
3. In `renderLiveTvWarnings` drop the `kidsUserProfileLink` line. Delete `renderSchedule` and its call in `load`.
4. In `renderHealth`, replace the `if (issue.href) { … }` block with:

```js
            if (issue.tab) {
                const fix = element('button', 'button-link', 'Fix');
                fix.type = 'button';
                fix.setAttribute('aria-label', 'Fix: ' + issue.text);
                fix.addEventListener('click', () => selectTab(issue.tab));
                line.append(fix);
            }
```

5. In `load`, call `selectTab(params.tab)` right before `form.hidden = false` (now `editor.hidden = false`).
6. Add the listeners next to the others:

```js
    view.querySelectorAll('.kidsTab').forEach((button) => {
        button.addEventListener('click', () => selectTab(button.dataset.tab));
        // Arrow keys move along the tabs, as in a tablist
        button.addEventListener('keydown', (event) => {
            const offset = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0;
            if (offset) {
                event.preventDefault();
                const next = tabNames[(tabNames.indexOf(button.dataset.tab) + offset + tabNames.length) % tabNames.length];
                selectTab(next);
                view.querySelector('#kidsTab-' + next).focus();
            }
        });
    });
    view.querySelector('.kidsGoProfile').addEventListener('click', (event) => {
        event.preventDefault();
        selectTab('profile');
    });
```

7. Update the file's header comment: drop "the allowed hours from Jellyfin (read-only)", and add that the editor has
   four tabs, Kids plus three that edit the kid's Jellyfin policy as jellyfin-web does.

**Step 3: Check**

Run: `node --check src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js && rg -n 'userTab|renderSchedule|form\.hidden|kidsUserProfileLink' src/Jellyfin.Plugin.Kids/Configuration/kidsUser.*`
Expected: no output.

Then on the dev server (`dev` redeploys the plugin on change; reload the browser): open alice's editor from
Dashboard → Kids. The four tabs show, Kids is selected and looks as before minus Allowed hours; the other three are
empty. Each Fix switches tab. On the Kids list page, alice's Fix links open the editor on the matching tab. The arrow
keys move between tabs.

**Step 4: Commit**

```bash
git add src/Jellyfin.Plugin.Kids/Configuration/kidsUser.html src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js
git commit -m "Split the kid editor into tabs"
```

---

### Task 3: Saving a Jellyfin tab, as jellyfin-web does

The shared part of the three Jellyfin tabs: per-tab snapshot for unsaved changes, and the save itself.

**Files:**
- Modify: `src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js`

**Step 1: The policy tabs registry and save**

Inside the default export, after `selectTab`:

```js
    // The Jellyfin tabs. Each has read() (its fields from the form, for unsaved changes), fill(policy) (the form from
    // a policy) and apply(policy) (its fields into a policy, the others untouched); Tasks 4 to 7 register them
    const policyTabs = {};
    const policySnapshots = {};

    function policyTabDirty(tab) {
        return policySnapshots[tab] !== undefined && JSON.stringify(policyTabs[tab].read()) !== policySnapshots[tab];
    }

    function renderTabDirty() {
        view.querySelectorAll('.kidsTab').forEach((button) => {
            const tab = button.dataset.tab;
            const dirty = tab === 'kids' ? isDirty() : policyTabs[tab] !== undefined && policyTabDirty(tab);
            button.querySelector('.kidsTabUnsaved').hidden = !dirty;
            const unsaved = view.querySelector('.kidsPanel[data-tab="' + tab + '"] .kidsUnsaved');
            if (unsaved) {
                unsaved.hidden = !dirty;
            }
        });
    }

    function fillPolicyTabs() {
        for (const [tab, entry] of Object.entries(policyTabs)) {
            entry.fill(user.Policy);
            policySnapshots[tab] = JSON.stringify(entry.read());
        }

        renderTabDirty();
    }

    // Like jellyfin-web's user tabs: read the user again so the write starts from the current policy, change only this
    // tab's fields, and send the whole policy back. Fields no tab shows (tags, devices, …) go back as they came.
    function savePolicyTab(tab, event) {
        event.preventDefault();
        const error = view.querySelector('.kidsPanel[data-tab="' + tab + '"] .kidsFormError');
        setLine(error, null);
        Dashboard.showLoadingMsg();
        ApiClient.getUser(userId)
            .then((current) => {
                policyTabs[tab].apply(current.Policy);
                return ApiClient.updateUserPolicy(userId, current.Policy).then(() => current);
            })
            .then((current) => {
                user = current;
                policyTabs[tab].fill(user.Policy);
                policySnapshots[tab] = JSON.stringify(policyTabs[tab].read());
                renderTabDirty();
                renderLiveTvWarnings();
                renderHealth(pickedIds);
                toast('Settings saved');
            })
            .catch((response) => {
                // Jellyfin answers a refused change (the last administrator, for example) with its reason as text
                const reason = response && typeof response.text === 'function' ? response.text() : Promise.resolve('');
                return reason.catch(() => '').then((text) => {
                    setLine(error, 'Couldn\'t save to Jellyfin' + (text ? ': ' + text : '.') + ' Nothing was changed.');
                });
            })
            .finally(() => Dashboard.hideLoadingMsg());
        return false;
    }
```

**Step 2: Track the saved channel ids, and hook dirty tracking**

1. Add `let pickedIds = []; // the live TV channels as last loaded or saved, for the health lines` with the other
   editor state, set it in `applyProfile` (`pickedIds = profile.LiveTvChannelIds || [];`, then
   `renderHealth(pickedIds)`) and in `save` (`pickedIds = profile.LiveTvChannelIds || []; renderHealth(pickedIds);`).
2. In `renderDirty`, call `renderTabDirty()` after setting `.kidsUnsaved`.
3. In `load`, call `fillPolicyTabs()` after `user = loadedUser;` has been set and `renderLiveTvWarnings()` has run.
4. In `onBeforeUnload`, check `isDirty() || Object.keys(policyTabs).some(policyTabDirty)`.

**Step 3: Check**

Run: `node --check src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js`
Expected: no output. The editor still loads and the Kids tab still saves (nothing is registered yet).

**Step 4: Commit**

```bash
git add src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js
git commit -m "Save a kid's Jellyfin policy one tab at a time"
```

---

### Task 4: Profile tab

**Files:**
- Modify: `src/Jellyfin.Plugin.Kids/Configuration/kidsUser.html` (`#kidsPanel-profile`)
- Modify: `src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js`

**Step 1: Markup**

Fill `#kidsPanel-profile`:

```html
                        <form class="kidsPolicyForm" data-tab="profile">
                            <p class="fieldDescription kidsAccountNote">These are <span class="kidsUserName"></span>'s Jellyfin account settings, the same as in Dashboard → Users. <a is="emby-linkbutton" class="button-link kidsJellyfinUserLink" href="#">More settings in Jellyfin</a></p>
                            <div class="checkboxContainer checkboxContainer-withDescription kidsAdminBlock" hidden>
                                <label>
                                    <input is="emby-checkbox" type="checkbox" class="kidsIsAdministrator" />
                                    <span>Allow this user to manage the server</span>
                                </label>
                                <div class="fieldDescription checkboxFieldDescription">Administrators skip every kids rule. Untick it for a kid.</div>
                            </div>
                            <div class="checkboxContainer checkboxContainer-withDescription">
                                <label>
                                    <input is="emby-checkbox" type="checkbox" class="kidsLiveTvAccess" />
                                    <span>Allow Live TV access</span>
                                </label>
                                <div class="fieldDescription checkboxFieldDescription">The kid still only gets the channels ticked on the Kids tab.</div>
                            </div>
                            <div class="checkboxContainer checkboxContainer-withDescription">
                                <label>
                                    <input is="emby-checkbox" type="checkbox" class="kidsDownloads" />
                                    <span>Allow media downloading</span>
                                </label>
                                <div class="fieldDescription checkboxFieldDescription">Downloads watched offline can't be stopped by the server.</div>
                            </div>
                            <p class="kidsFormError" role="status" aria-live="polite" hidden></p>
                            <p class="kidsUnsaved" role="status" aria-live="polite" hidden>Unsaved changes</p>
                            <button is="emby-button" type="submit" class="raised button-submit block"><span>Save</span></button>
                        </form>
```

The account note is the same in all three Jellyfin tabs: copy it into Tasks 5 and 6's markup as is.

**Step 2: JavaScript**

After `savePolicyTab`:

```js
    // Profile. The administrator box shows only while the kid is one, so it can be turned off; the editor never makes a
    // kid an administrator
    policyTabs.profile = {
        read: () => ({
            admin: view.querySelector('.kidsIsAdministrator').checked,
            liveTv: view.querySelector('.kidsLiveTvAccess').checked,
            downloads: view.querySelector('.kidsDownloads').checked
        }),
        fill: (policy) => {
            view.querySelector('.kidsAdminBlock').hidden = !policy.IsAdministrator;
            view.querySelector('.kidsIsAdministrator').checked = Boolean(policy.IsAdministrator);
            view.querySelector('.kidsLiveTvAccess').checked = policy.EnableLiveTvAccess !== false;
            view.querySelector('.kidsDownloads').checked = Boolean(policy.EnableContentDownloading);
        },
        apply: (policy) => {
            const fields = policyTabs.profile.read();
            policy.IsAdministrator = policy.IsAdministrator && fields.admin;
            policy.EnableLiveTvAccess = fields.liveTv;
            policy.EnableContentDownloading = fields.downloads;
        }
    };
```

In `load`, after the title is set: fill every `.kidsUserName` with `user.Name` (`textContent`) and set every
`.kidsJellyfinUserLink` href to `'#/dashboard/users/' + userId + '/profile'`.

Add the form listeners next to the others (one block, reused by every Jellyfin tab):

```js
    view.querySelectorAll('.kidsPolicyForm').forEach((policyForm) => {
        policyForm.addEventListener('submit', (event) => savePolicyTab(policyForm.dataset.tab, event));
        policyForm.addEventListener('input', renderTabDirty);
        policyForm.addEventListener('change', renderTabDirty);
    });
```

**Step 3: Check**

Run: `node --check src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js`

On the dev server, as admin:
1. In alice's editor, Profile: tick "Allow media downloading", see the dot on the tab and "Unsaved changes"; Save.
   "Settings saved", the dot goes, and "Downloads are allowed" appears in the health list. Dashboard → Users → alice →
   Profile shows downloads on. Untick and save: the line goes.
2. Untick Live TV, save: the Kids tab shows the "Live TV access is off" notice.
3. Make bob an administrator in Jellyfin, open bob's editor: the administrator box shows ticked. Untick, save: the
   administrator issue goes and Jellyfin shows bob as a normal user.

**Step 4: Commit**

```bash
git add src/Jellyfin.Plugin.Kids/Configuration/kidsUser.html src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js
git commit -m "Edit a kid's Jellyfin profile permissions from the kid editor"
```

---

### Task 5: Access tab

**Files:**
- Modify: `src/Jellyfin.Plugin.Kids/Configuration/kidsUser.html` (`#kidsPanel-access`)
- Modify: `src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js` (`load`, the registry)

**Step 1: Markup**

```html
                        <form class="kidsPolicyForm" data-tab="access">
                            <!-- the account note from Task 4 -->
                            <div class="checkboxContainer">
                                <label>
                                    <input is="emby-checkbox" type="checkbox" class="kidsAllFolders" />
                                    <span>Enable access to all libraries</span>
                                </label>
                            </div>
                            <div class="kidsFolderBlock">
                                <h3 class="checkboxListLabel">Libraries</h3>
                                <div class="checkboxList paperList kidsFolders"></div>
                                <p class="fieldDescription">The libraries this kid sees, on every app.</p>
                            </div>
                            <p class="kidsFormError" role="status" aria-live="polite" hidden></p>
                            <p class="kidsUnsaved" role="status" aria-live="polite" hidden>Unsaved changes</p>
                            <button is="emby-button" type="submit" class="raised button-submit block"><span>Save</span></button>
                        </form>
```

Add `#KidsUserPage .kidsFolderBlock[hidden] { display: none; }` to the styles.

**Step 2: JavaScript**

1. Add `let mediaFolders = []; // the server's libraries: { Id, Name }` to the editor state. In `load`, fetch them
   alongside the rest, as jellyfin-web's Access tab does:

```js
        const libraries = ApiClient.getJSON(ApiClient.getUrl('Library/MediaFolders', { IsHidden: false }))
            .then((result) => result.Items || []);
```

   and add it to the `Promise.all`; set `mediaFolders` from it before `fillPolicyTabs()`. It is required, like the user:
   without it the Access tab could not show the kid's libraries.

2. The tab:

```js
    // Access: all libraries, or the ticked ones. A stored library the server no longer has is kept, as jellyfin-web does
    function renderFolders(enabledIds) {
        const enabled = new Set(enabledIds);
        view.querySelector('.kidsFolders').replaceChildren(...mediaFolders.map((folder) => {
            const label = element('label');
            const checkbox = element('input', 'kidsFolder');
            checkbox.type = 'checkbox';
            checkbox.setAttribute('is', 'emby-checkbox');
            checkbox.dataset.id = folder.Id;
            checkbox.checked = enabled.has(folder.Id);
            label.append(checkbox, element('span', null, folder.Name));
            return label;
        }));
    }

    function renderFolderBlock() {
        view.querySelector('.kidsFolderBlock').hidden = view.querySelector('.kidsAllFolders').checked;
    }

    policyTabs.access = {
        read: () => ({
            all: view.querySelector('.kidsAllFolders').checked,
            folders: [...view.querySelectorAll('.kidsFolder')].filter((box) => box.checked).map((box) => box.dataset.id)
        }),
        fill: (policy) => {
            view.querySelector('.kidsAllFolders').checked = Boolean(policy.EnableAllFolders);
            renderFolders(policy.EnabledFolders || []);
            renderFolderBlock();
        },
        apply: (policy) => {
            const fields = policyTabs.access.read();
            policy.EnableAllFolders = fields.all;
            policy.EnabledFolders = fields.all ? [] : fields.folders;
        }
    };
```

3. Listener: `view.querySelector('.kidsAllFolders').addEventListener('change', renderFolderBlock);`

Note: the folder ids from `Library/MediaFolders` and `EnabledFolders` are the same GUIDs, but compare them the way
the server writes them. Check one on the dev server (Step 3.1) before moving on; if their formats differ (dashes,
case), normalise both with `id.replace(/-/g, '').toLowerCase()` in `renderFolders` and `read`.

**Step 3: Check**

Run: `node --check src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js`

On the dev server:
1. Access shows "Enable access to all libraries" ticked for alice and "Sees every library" in the health list.
   Untick it: the library list shows. Tick one library, Save: the issue goes; Dashboard → Users → alice → Access shows
   the same library. Reload the editor: the same library is ticked.
2. As alice in jellyfin-web, only that library shows.

**Step 4: Commit**

```bash
git add src/Jellyfin.Plugin.Kids/Configuration/kidsUser.html src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js
git commit -m "Choose a kid's libraries from the kid editor"
```

---

### Task 6: Parental control tab, rating and unrated items

**Files:**
- Modify: `src/Jellyfin.Plugin.Kids/Configuration/kidsUser.html` (`#kidsPanel-parental`)
- Modify: `src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js` (module helpers, `load`, the registry)

**Step 1: Markup**

```html
                        <form class="kidsPolicyForm" data-tab="parental">
                            <!-- the account note from Task 4 -->
                            <div class="selectContainer">
                                <label class="selectLabel" for="kidsMaxRating">Maximum allowed parental rating</label>
                                <select is="emby-select" id="kidsMaxRating" class="emby-select-withcolor emby-select kidsMaxRating" aria-describedby="kidsMaxRatingHelp"></select>
                                <div class="selectArrowContainer"><div style="visibility:hidden;display:none;">0</div><span class="selectArrow material-icons keyboard_arrow_down" aria-hidden="true"></span></div>
                                <div class="fieldDescription" id="kidsMaxRatingHelp">Content with a higher rating is hidden from this kid.</div>
                            </div>
                            <div class="verticalSection">
                                <h3 class="checkboxListLabel">Block items with no or unrecognized rating information</h3>
                                <div class="checkboxList paperList kidsUnrated"></div>
                            </div>
                            <!-- Task 7 adds Allowed hours here -->
                            <p class="kidsFormError" role="status" aria-live="polite" hidden></p>
                            <p class="kidsUnsaved" role="status" aria-live="polite" hidden>Unsaved changes</p>
                            <button is="emby-button" type="submit" class="raised button-submit block"><span>Save</span></button>
                        </form>
```

**Step 2: Module helpers** (next to `scheduleText`, outside the default export)

```js
// Jellyfin's ratings, one entry per score as jellyfin-web lists them ("G/TV-G"): { name, score, subScore }
function groupRatings(ratings) {
    const groups = [];
    for (const rating of ratings || []) {
        const score = rating.RatingScore ? rating.RatingScore.score : null;
        const subScore = rating.RatingScore && rating.RatingScore.subScore !== undefined ? rating.RatingScore.subScore : null;
        const last = groups[groups.length - 1];
        if (last && last.score === score && last.subScore === subScore) {
            last.name += '/' + rating.Name;
        } else {
            groups.push({ name: rating.Name, score, subScore });
        }
    }

    return groups;
}

// The group a policy's maximum stands for, as jellyfin-web picks it: the exact score and sub score, else the highest
// score at or under the maximum. -1 when there is no maximum.
function ratingIndex(groups, policy) {
    if (policy.MaxParentalRating === undefined || policy.MaxParentalRating === null) {
        return -1;
    }

    const subScore = policy.MaxParentalSubRating === undefined ? null : policy.MaxParentalSubRating;
    const exact = groups.findIndex((group) => group.score === policy.MaxParentalRating && group.subScore === subScore);
    if (exact >= 0) {
        return exact;
    }

    let index = -1;
    groups.forEach((group, i) => {
        if (group.score !== null && group.score <= policy.MaxParentalRating) {
            index = i;
        }
    });
    return index;
}

// The item types Jellyfin can block when unrated, in jellyfin-web's order
const unratedTypes = [
    { id: 'Book', name: 'Books' },
    { id: 'ChannelContent', name: 'Channels' },
    { id: 'LiveTvChannel', name: 'Live TV' },
    { id: 'Movie', name: 'Movies' },
    { id: 'Music', name: 'Music' },
    { id: 'Trailer', name: 'Trailers' },
    { id: 'Series', name: 'Shows' }
];
```

Check the two helpers before wiring them, with the dev server's real ratings:

```bash
curl -s -H "Authorization: MediaBrowser Token=\"$(cat .dev/admin.token)\"" http://127.0.0.1:8096/Localization/ParentalRatings >/tmp/claude-1000/-home-aldo-Dev-aldoborrero-jellyfin-kids/358ef9e9-19e7-4b0f-b948-f7fab261ca40/scratchpad/ratings.json
node --input-type=module -e "
import { readFileSync } from 'node:fs';
const src = readFileSync('src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js', 'utf8');
const pick = (name) => src.slice(src.indexOf('function ' + name), src.indexOf('\n}\n', src.indexOf('function ' + name)) + 2);
const groupRatings = new Function(pick('groupRatings') + 'return groupRatings;')();
const ratingIndex = new Function(pick('ratingIndex') + 'return ratingIndex;')();
const groups = groupRatings(JSON.parse(readFileSync(process.argv[1], 'utf8')));
console.log(groups.slice(0, 6));
console.log(ratingIndex(groups, { MaxParentalRating: null }), ratingIndex(groups, { MaxParentalRating: groups[3].score, MaxParentalSubRating: groups[3].subScore }));
" /tmp/claude-1000/-home-aldo-Dev-aldoborrero-jellyfin-kids/358ef9e9-19e7-4b0f-b948-f7fab261ca40/scratchpad/ratings.json
```

Expected: groups with names joined by `/` where scores repeat, then `-1 3`.

**Step 3: The tab**

1. Add `let ratingGroups = [];` to the editor state. In `load`, add
   `ApiClient.getParentalRatings().then(groupRatings)` to the `Promise.all` (required, like the libraries) and set
   `ratingGroups` before `fillPolicyTabs()`.
2. Register:

```js
    // Parental control: the maximum rating ("None" for no maximum) and the unrated types to block
    function renderRatings(selected) {
        const select = view.querySelector('.kidsMaxRating');
        const none = element('option', null, 'None');
        none.value = '-1';
        select.replaceChildren(none, ...ratingGroups.map((group, index) => {
            const option = element('option', null, group.name);
            option.value = String(index);
            return option;
        }));
        select.value = String(selected);
    }

    function renderUnrated(blocked) {
        const set = new Set(blocked);
        view.querySelector('.kidsUnrated').replaceChildren(...unratedTypes.map((type) => {
            const label = element('label');
            const checkbox = element('input', 'kidsUnratedType');
            checkbox.type = 'checkbox';
            checkbox.setAttribute('is', 'emby-checkbox');
            checkbox.dataset.type = type.id;
            checkbox.checked = set.has(type.id);
            label.append(checkbox, element('span', null, type.name));
            return label;
        }));
    }

    policyTabs.parental = {
        read: () => ({
            rating: Number(view.querySelector('.kidsMaxRating').value),
            unrated: [...view.querySelectorAll('.kidsUnratedType')].filter((box) => box.checked).map((box) => box.dataset.type)
        }),
        fill: (policy) => {
            renderRatings(ratingIndex(ratingGroups, policy));
            renderUnrated(policy.BlockUnratedItems || []);
        },
        apply: (policy) => {
            const fields = policyTabs.parental.read();
            const group = ratingGroups[fields.rating];
            policy.MaxParentalRating = group ? group.score : null;
            policy.MaxParentalSubRating = group ? group.subScore : null;
            // Types this page doesn't list (a later Jellyfin's) stay as they were
            const known = new Set(unratedTypes.map((type) => type.id));
            policy.BlockUnratedItems = (policy.BlockUnratedItems || []).filter((type) => !known.has(type)).concat(fields.unrated);
        }
    };
```

**Step 4: Check**

Run: `node --check src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js`

On the dev server: Parental control for alice shows the same maximum and unrated ticks as Dashboard → Users → alice →
Parental control. Pick a maximum and tick Movies and Shows, Save: "No maximum parental rating" and "Unrated movies and
shows are not blocked" go, and Jellyfin's tab shows the same values. Set "None", Save: the issue comes back.

**Step 5: Commit**

```bash
git add src/Jellyfin.Plugin.Kids/Configuration/kidsUser.html src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js
git commit -m "Set a kid's maximum rating and unrated blocks from the kid editor"
```

---

### Task 7: Parental control tab, allowed hours

**Files:**
- Modify: `src/Jellyfin.Plugin.Kids/Configuration/kidsUser.html` (`#kidsPanel-parental`, styles)
- Modify: `src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js` (`policyTabs.parental`, listeners)

**Step 1: Markup**

Where Task 6 left `<!-- Task 7 adds Allowed hours here -->`:

```html
                            <div class="verticalSection">
                                <div class="sectionTitleContainer flex align-items-center">
                                    <h3 class="sectionTitle">Allowed hours</h3>
                                    <button is="emby-button" type="button" class="raised kidsScheduleAdd"><span>Add</span></button>
                                </div>
                                <p class="fieldDescription">In server time. Outside them the kid can't sign in or play on any app; with enforcement on, playing stops when they end.</p>
                                <div class="kidsSchedules paperList"></div>
                            </div>
```

And, at the end of `.kidsEditor` (outside every form, so its buttons never submit one):

```html
                    <dialog class="kidsScheduleDialog" aria-labelledby="kidsScheduleDialogTitle">
                        <form method="dialog" class="kidsScheduleForm">
                            <h3 id="kidsScheduleDialogTitle">Allowed hours</h3>
                            <div class="selectContainer">
                                <label class="selectLabel" for="kidsScheduleDay">Day</label>
                                <select is="emby-select" id="kidsScheduleDay" class="emby-select-withcolor emby-select kidsScheduleDay"></select>
                            </div>
                            <div class="selectContainer">
                                <label class="selectLabel" for="kidsScheduleStart">Start</label>
                                <select is="emby-select" id="kidsScheduleStart" class="emby-select-withcolor emby-select kidsScheduleStart"></select>
                            </div>
                            <div class="selectContainer">
                                <label class="selectLabel" for="kidsScheduleEnd">End</label>
                                <select is="emby-select" id="kidsScheduleEnd" class="emby-select-withcolor emby-select kidsScheduleEnd"></select>
                            </div>
                            <p class="kidsScheduleError kidsFormError" role="status" aria-live="polite" hidden></p>
                            <div class="kidsDialogButtons">
                                <button is="emby-button" type="button" class="raised kidsScheduleCancel"><span>Cancel</span></button>
                                <button is="emby-button" type="submit" class="raised button-submit kidsScheduleSave"><span>Add</span></button>
                            </div>
                        </form>
                    </dialog>
```

Styles:

```css
            #KidsUserPage .kidsScheduleDialog { background: var(--jf-palette-background-paper, #202020); color: inherit; border: 0; border-radius: var(--jf-card-borderRadius, 0.2em); padding: 1.5em; width: min(24em, calc(100vw - 2em)); box-sizing: border-box; }
            #KidsUserPage .kidsScheduleDialog::backdrop { background: rgba(0, 0, 0, 0.6); }
            #KidsUserPage .kidsDialogButtons { display: flex; justify-content: flex-end; gap: 0.75em; }
            #KidsUserPage .kidsDialogButtons .emby-button { margin: 0; }
```

Each `.kidsFormError` in a policy form is found with `.kidsPanel[data-tab=…] .kidsFormError`; the dialog sits outside
the panels, so it doesn't clash.

**Step 2: JavaScript**

1. Add `let schedules = []; // the Parental control tab's allowed hours: { DayOfWeek, StartHour, EndHour }` to the
   editor state.
2. Rendering, the dialog and the tab:

```js
    function renderSchedules() {
        const list = view.querySelector('.kidsSchedules');
        if (schedules.length === 0) {
            list.replaceChildren(element('div', 'fieldDescription kidsEmpty', 'Any time: no allowed hours are set.'));
            return;
        }

        list.replaceChildren(...schedules.map((schedule, index) => {
            const item = row('listItem-border');
            const body = element('div', 'listItemBody');
            body.append(element('div', 'listItemBodyText', scheduleText(schedule)));
            item.append(body, iconButton('delete', 'Remove ' + scheduleText(schedule), () => {
                schedules.splice(index, 1);
                renderSchedules();
                renderTabDirty();
            }));
            return item;
        }));
    }

    const scheduleDialog = view.querySelector('.kidsScheduleDialog');

    function hourOptions(select, from, to) {
        const options = [];
        for (let hour = from; hour <= to; hour += 0.5) {
            const option = element('option', null, hourText(hour));
            option.value = String(hour);
            options.push(option);
        }

        select.replaceChildren(...options);
    }

    function openScheduleDialog() {
        const day = view.querySelector('.kidsScheduleDay');
        day.replaceChildren(...['Everyday', 'Weekday', 'Weekend', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']
            .map((id) => {
                const option = element('option', null, scheduleDays[id]);
                option.value = id;
                return option;
            }));
        hourOptions(view.querySelector('.kidsScheduleStart'), 0, 23.5);
        hourOptions(view.querySelector('.kidsScheduleEnd'), 0.5, 24);
        view.querySelector('.kidsScheduleStart').value = '8';
        view.querySelector('.kidsScheduleEnd').value = '20';
        setLine(view.querySelector('.kidsScheduleError'), null);
        scheduleDialog.showModal();
    }

    function addSchedule(event) {
        event.preventDefault();
        const start = Number(view.querySelector('.kidsScheduleStart').value);
        const end = Number(view.querySelector('.kidsScheduleEnd').value);
        if (end <= start) {
            setLine(view.querySelector('.kidsScheduleError'), 'The end must be after the start.');
            return;
        }

        schedules.push({ DayOfWeek: view.querySelector('.kidsScheduleDay').value, StartHour: start, EndHour: end });
        scheduleDialog.close();
        renderSchedules();
        renderTabDirty();
    }
```

3. Extend `policyTabs.parental` from Task 6:
   - `read` also returns `schedules: schedules.map((s) => [s.DayOfWeek, s.StartHour, s.EndHour])`;
   - `fill` also does `schedules = (policy.AccessSchedules || []).map((s) => ({ DayOfWeek: s.DayOfWeek, StartHour: s.StartHour, EndHour: s.EndHour })); renderSchedules();`;
   - `apply` also sets `policy.AccessSchedules = schedules.map((s) => ({ DayOfWeek: s.DayOfWeek, StartHour: s.StartHour, EndHour: s.EndHour }));`.

   The schedules keep the server's `DayOfWeek` as it came (a name; `scheduleText` already handles a number too).
   Each new entry has no `Id` or `UserId`: Jellyfin assigns them, as with jellyfin-web's dialog.

4. Listeners:

```js
    view.querySelector('.kidsScheduleAdd').addEventListener('click', openScheduleDialog);
    view.querySelector('.kidsScheduleForm').addEventListener('submit', addSchedule);
    view.querySelector('.kidsScheduleCancel').addEventListener('click', () => scheduleDialog.close());
```

**Step 3: Check**

Run: `node --check src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js`

On the dev server:
1. Parental control lists alice's allowed hours as Jellyfin does. Add "Weekdays 08:00–20:00": the dot shows; Save.
   Jellyfin's Parental control tab lists the same slot. End before start shows "The end must be after the start.".
2. Remove the slot, Save: "Any time" shows, and Jellyfin's tab is empty.
3. With enforcement on, add a slot ending in 2 minutes and play as alice: the bedtime warning and stop follow it
   (scripts/dev-check.sh's check m covers the server side of this).
4. Escape and Cancel close the dialog without adding anything; Tab stays inside the dialog while it is open.

**Step 4: Commit**

```bash
git add src/Jellyfin.Plugin.Kids/Configuration/kidsUser.html src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js
git commit -m "Edit a kid's allowed hours from the kid editor"
```

---

### Task 8: Documentation

**Files:**
- Modify: `specs/001-kids-plugin-foundation/contracts/dashboard-pages.md` (KidsUser section)
- Modify: `specs/001-kids-plugin-foundation/quickstart.md` (section 5)
- Modify: `README.md` (What it does → Dashboard pages; Live TV, "open the kid in Dashboard → Kids")
- Modify: `docs/install.md` only if it tells parents to set libraries, ratings or hours in Jellyfin's user pages (`rg -n -i 'parental|librar|schedule' docs/install.md`)
- Replace: `docs/assets/screenshots/kid-editor-dark.png`

**Step 1: Contract.** In `dashboard-pages.md` → KidsUser, add a **Tabs** list: Kids (everything above), Profile,
Access, Parental control with their fields; the URL's optional `&tab=kids|profile|access|parental`; **Load** gains
`Library/MediaFolders?IsHidden=false` and `Localization/ParentalRatings`; a **Save (Jellyfin tabs)** entry: re-read the
user, change only the tab's fields (list them as in the design), `POST /Users/{id}/Policy`, "Settings saved"; on an
error the reason inline, nothing changed. Health issues open the editor's tab (in the editor and from the Kids page).

**Step 2: Quickstart.** Add to section 5, after step 6, the checklist from the design's Testing section, including
the "nothing is lost" step: set an allowed tag and limit alice to one device in Jellyfin, save each Jellyfin tab in the
editor, and Jellyfin still shows the tag and the device.

**Step 3: README.** Dashboard pages row: "… with a link to fix it" becomes "and each fix is on the kid's own page:
libraries, maximum rating, unrated items, allowed hours, live TV access and downloads are edited there, next to the
kids settings". Live TV section: "Turn on Allow Live TV access on the kid's Profile tab".

**Step 4: Screenshot.** Retake `kid-editor-dark.png` on the dev server at the same size as the current one
(`file docs/assets/screenshots/kid-editor-dark.png`), Kids tab selected; update its alt text in the README to
mention the tabs.

**Step 5: Commit**

```bash
git add specs/001-kids-plugin-foundation README.md docs
git commit -m "Document the kid editor's Jellyfin tabs"
```

---

### Task 9: Full check

**Step 1:** `nix develop -c dotnet test Jellyfin.Plugin.Kids.slnx --configuration Release`
Expected: 547 passed, 0 failed (the server is unchanged).

**Step 2:** `nix develop -c dotnet build Jellyfin.Plugin.Kids.slnx --configuration Release`
Expected: no warnings (`TreatWarningsAsErrors`).

**Step 3:** With `dev` up: `scripts/dev-check.sh`
Expected: every check PASS.

**Step 4:** Run the whole quickstart section 5 checklist from Task 8, in the dark and light themes and at phone
width (the tabs wrap, the dialog fits).

**Step 5:** Report what passed and anything that didn't. Versioning and the release are a separate step (bump
`build.yaml` and `Directory.Build.props`, write the changelog, tag), not part of this plan.
