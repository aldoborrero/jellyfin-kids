# Contract: Parent lock section on the Kids dashboard page

This extends the `Kids` page from [feature 1](../../001-kids-plugin-foundation/contracts/dashboard-pages.md). The
HTML is still static and anonymous (FR-025). Data loads on `viewshow` through `window.ApiClient` with the
administrator's session, so no grant is involved.

## Placement

A "Parent lock" section above the users list, in `kids.html` and `kids.js`. No new page and no new menu entry.

## Status

- **Load**: `GET /Kids/Parent/Lock`.
  - `{}`: "No parent lock is set." Add the recommendation: "Set a code so grown-ups can unlock kids mode on TVs.
    Without one, only an administrator sign-in can change kids settings from a TV."
  - Otherwise: "Parent lock: Digits" or "Parent lock: Remote buttons", followed by "last changed <local date and
    time>".
  - A 500 shows an error in place of the status, never "no lock".

## Set or replace

- **Fields**:
  - type select: Digits (`NUMERIC`) or Remote buttons (`REMOTE`). Defaults to the current type, or Remote buttons
    when there is no lock;
  - code and confirmation: `type="password"`, `autocomplete="off"`, `maxlength="32"`.
- **Help text** for Remote buttons lists the symbols: U D L R (arrows), 0–9, r g y b (colour buttons), p (play/pause),
  w (rewind), f (fast-forward), + − (channel up and down), i (info). It notes that the code is case-sensitive. A
  recommendation reads: "Longer codes are safer; TVs keep a copy to unlock offline."
- **Client-side checks**, mirroring the contract: 4–32 symbols, only the type's set, and both fields equal. On
  failure, show an inline message and send nothing.
- **Save**: `PUT /Kids/Parent/Lock {"LockType":…, "Code":…}`.
  - 200: clear both fields, show the new status and "Saved".
  - 400: show `errors.Code` (or `LockType`) next to the field.
  - Anything else: `Dashboard.alert`.
  - The code is never shown again, and the page never requests the verifier.

## Remove

- Shown only when a lock is set.
- `Dashboard.confirm` ("Remove the parent lock? TVs will need an administrator sign-in to change kids settings."),
  then `DELETE /Kids/Parent/Lock`.
- 204 or 404: show "No parent lock is set."
