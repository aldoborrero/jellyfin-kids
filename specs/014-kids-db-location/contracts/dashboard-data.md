# Contract: Kids page Data section (feature 14)

The last section of Dashboard → Kids, after Screen time. Read-only: no inputs or buttons.

## Markup (static, in `kids.html`)

```text
Data                                                          (h3 sectionTitle)
Kid profiles, the parent code, screen time and channels are kept in this folder on
the server. Back it up with Jellyfin's data; Jellyfin's own backup does not include
it. Don't delete it: that loses every kid's settings.        (fieldDescription)

[folder icon]  /var/lib/jellyfin/data/kids                    (listItem, path as text)
               Database: /var/lib/jellyfin/data/kids/kids.db  (secondary)
```

The page HTML holds no path; the two lines are empty until `kids.js` loads them.

## Behaviour (`kids.js`)

- On `viewshow`, `GET Kids/Admin/Storage`.
- 200 and `Available`: the folder in the first line, `Database: <DatabasePath>` in the second.
- 200 and not `Available`: the folder, and the second line in the error colour: "The database could not be opened.
  Check the server log."
- Error: "Could not load the data location." in the error colour, no path.
- Paths are inserted with `textContent`, wrapping anywhere (long paths on phones).
