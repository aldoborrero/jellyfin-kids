using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jellyfin.Plugin.Kids.Profiles;

/// <summary>
/// Validates and normalises kid profile settings (data-model.md "Validation").
/// </summary>
public static class KidProfileValidator
{
    /// <summary>
    /// Validates settings sent by a client.
    /// </summary>
    /// <param name="input">The settings as sent.</param>
    /// <param name="storedPicks">The picks already stored for this kid; they are kept even if the item was deleted.</param>
    /// <param name="catalog">The server's collections and playlists, used to check new picks.</param>
    /// <returns>The normalised settings, or every error keyed by field.</returns>
    public static ValidationResult Validate(KidProfileInput input, IReadOnlyCollection<Guid> storedPicks, IPickCatalog catalog)
    {
        var errors = new Dictionary<string, string[]>();

        var cap = input.AutoplayCap ?? KidProfileSettings.DefaultAutoplayCap;
        if (cap is < KidProfileSettings.MinAutoplayCap or > KidProfileSettings.MaxAutoplayCap)
        {
            errors["AutoplayCap"] = [string.Create(CultureInfo.InvariantCulture, $"Must be between {KidProfileSettings.MinAutoplayCap} and {KidProfileSettings.MaxAutoplayCap}.")];
        }

        var allowance = input.DailyAllowanceMinutes;
        if (allowance is < KidProfileSettings.MinDailyAllowance or > KidProfileSettings.MaxDailyAllowance)
        {
            errors["DailyAllowanceMinutes"] = [string.Create(CultureInfo.InvariantCulture, $"Must be between {KidProfileSettings.MinDailyAllowance} and {KidProfileSettings.MaxDailyAllowance}.")];
        }

        var sections = ValidateSections(input.Sections ?? [], errors);
        var picks = ValidatePicks(input.PickedItemIds ?? [], storedPicks, catalog, errors);

        if (errors.Count > 0)
        {
            return new ValidationResult(null, errors);
        }

        var settings = new KidProfileSettings
        {
            SearchAllowed = input.SearchAllowed ?? false,
            AutoplayEnabled = input.AutoplayEnabled ?? false,
            AutoplayCap = cap,
            Sections = HomeSectionEntry.Normalize(sections),
            PickedItemIds = picks,
            DailyAllowanceMinutes = allowance
        };
        return new ValidationResult(settings, errors);
    }

    private static List<HomeSectionEntry> ValidateSections(IReadOnlyList<SectionInput> sections, Dictionary<string, string[]> errors)
    {
        var names = Enum.GetNames<HomeSection>();
        var entries = new List<HomeSectionEntry>();
        for (var i = 0; i < sections.Count; i++)
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"Sections[{i}]");
            var value = sections[i].Section;

            // Match whole names only: Enum.TryParse would also take numbers and comma-separated lists
            var name = names.FirstOrDefault(n => string.Equals(n, value?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name is null)
            {
                errors[key] = [$"Unknown section '{value}'."];
                continue;
            }

            var section = Enum.Parse<HomeSection>(name);
            if (entries.Exists(e => e.Section == section))
            {
                errors[key] = [$"Section '{name}' appears more than once."];
                continue;
            }

            entries.Add(new HomeSectionEntry(section, sections[i].Visible ?? true));
        }

        return entries;
    }

    private static List<Guid> ValidatePicks(IReadOnlyList<Guid> picks, IReadOnlyCollection<Guid> storedPicks, IPickCatalog catalog, Dictionary<string, string[]> errors)
    {
        if (picks.Count > KidProfileSettings.MaxPicks)
        {
            errors["PickedItemIds"] = [string.Create(CultureInfo.InvariantCulture, $"At most {KidProfileSettings.MaxPicks} picks.")];
            return [];
        }

        var stored = storedPicks.ToHashSet();
        var resolved = catalog.Resolve(picks.Where(id => !stored.Contains(id)).Distinct()).Select(c => c.Id).ToHashSet();
        var seen = new HashSet<Guid>();
        for (var i = 0; i < picks.Count; i++)
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"PickedItemIds[{i}]");
            if (!seen.Add(picks[i]))
            {
                errors[key] = ["Picked more than once."];
            }
            else if (!stored.Contains(picks[i]) && !resolved.Contains(picks[i]))
            {
                errors[key] = ["Not a collection or playlist."];
            }
        }

        return [.. picks];
    }
}
