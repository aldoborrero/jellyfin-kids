using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Allowance;
using Jellyfin.Plugin.Kids.Enforcement;
using Jellyfin.Plugin.Kids.Profiles;
using Jellyfin.Plugin.Kids.Stats;
using Jellyfin.Plugin.Kids.Storage;
using Jellyfin.Plugin.Kids.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

/// <summary>
/// The statistics service over a real database, shared by the controller tests.
/// </summary>
public sealed class StatsFixture : IDisposable
{
    private readonly TempFolder _folder = new();

    public StatsFixture()
    {
        Database = new KidsDatabase(_folder.Path, NullLogger<KidsDatabase>.Instance);
        UsageStore = new UsageStore(Database);
        Store = new StatsStore(Database);
        Profiles = new KidProfileService(new KidProfileStore(Database), Users, new FakePickCatalog(), new FakeLiveTvChannelCatalog(), Roster);
        Usage = new UsageService(UsageStore, Profiles, Settings, Time, NullLogger<UsageService>.Instance);
        Watch = new WatchLog(Store, Roster, Settings, NullLogger<WatchLog>.Instance);
        Service = new StatsService(Store, Usage, Watch, Profiles, Settings, Time);

        // 2026-11-19 18:00 UTC, days from midnight UTC
        Time.Now = new DateTimeOffset(2026, 11, 19, 18, 0, 0, TimeSpan.Zero);
    }

    public FakeTimeProvider Time { get; } = new();

    public FakeKidsSettings Settings { get; } = new();

    public FakeUserDirectory Users { get; } = new();

    public KidRoster Roster { get; } = new();

    public KidsDatabase Database { get; }

    public UsageStore UsageStore { get; }

    public StatsStore Store { get; }

    public KidProfileService Profiles { get; }

    public UsageService Usage { get; }

    public WatchLog Watch { get; }

    public StatsService Service { get; }

    public async Task<Guid> MarkAsync(int? allowance, Guid? id = null)
    {
        var kid = id ?? Guid.NewGuid();
        Users.Users.Add(kid);
        var result = await Profiles.SaveAsync(kid, 0, new KidProfileInput { DailyAllowanceMinutes = allowance });
        Assert.Equal(SaveOutcome.Saved, result.Outcome);
        Roster.Add(kid);
        return kid;
    }

    public void Play(Guid kid, WatchItem item, DateTimeOffset from, int seconds, string session = "s1", string play = "p1", string device = "Salón TV")
    {
        Usage.AddPlayed(kid, from, from.AddSeconds(seconds));
        Watch.Played(session, play, kid, item, device, "Peke", from, from.AddSeconds(seconds));
    }

    public void Dispose()
    {
        Watch.Dispose();
        Usage.Dispose();
        Database.Dispose();
        _folder.Dispose();
    }
}
