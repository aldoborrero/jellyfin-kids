using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// Which playback actions the convention guarded, and which expected ones the server does not have (research R10).
/// One instance is shared by the convention, which runs while MVC builds its action list, and the startup check.
/// </summary>
public sealed class GateCoverage
{
    private readonly Lock _lock = new();
    private readonly List<(string Controller, string Action, GateKind Kind, bool ByName)> _matched = [];
    private bool _checked;

    /// <summary>
    /// The state of the startup check.
    /// </summary>
    public enum CheckState
    {
        /// <summary>The check has not run yet.</summary>
        Pending,

        /// <summary>Every expected action is guarded.</summary>
        Ok,

        /// <summary>Some expected actions were not found.</summary>
        Degraded
    }

    /// <summary>
    /// Gets the guarded actions.
    /// </summary>
    public IReadOnlyList<(string Controller, string Action, GateKind Kind, bool ByName)> Matched
    {
        get
        {
            lock (_lock)
            {
                return _matched.ToList();
            }
        }
    }

    /// <summary>
    /// Gets the expected actions that were not guarded, as <c>Controller.Action</c>.
    /// </summary>
    public IReadOnlyList<string> Missing
    {
        get
        {
            lock (_lock)
            {
                return GateActions.Expected
                    .Where(e => !_matched.Any(m => m.Controller == e.Controller && m.Action == e.Action))
                    .Select(e => e.Controller + "." + e.Action)
                    .ToList();
            }
        }
    }

    /// <summary>
    /// Gets the state of the startup check.
    /// </summary>
    public CheckState State
    {
        get
        {
            lock (_lock)
            {
                if (!_checked)
                {
                    return CheckState.Pending;
                }
            }

            return Missing.Count == 0 ? CheckState.Ok : CheckState.Degraded;
        }
    }

    /// <summary>
    /// Records a guarded action.
    /// </summary>
    /// <param name="controller">The controller name.</param>
    /// <param name="action">The action name.</param>
    /// <param name="kind">What the action does.</param>
    /// <param name="byName">Whether it matched by name, rather than only by its media attribute.</param>
    public void Add(string controller, string action, GateKind kind, bool byName)
    {
        lock (_lock)
        {
            if (!_matched.Any(m => m.Controller == controller && m.Action == action))
            {
                _matched.Add((controller, action, kind, byName));
            }
        }
    }

    /// <summary>
    /// Marks the startup check done, so <see cref="State"/> reports its result.
    /// </summary>
    public void MarkChecked()
    {
        lock (_lock)
        {
            _checked = true;
        }
    }
}
