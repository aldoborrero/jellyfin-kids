using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Kids.LiveTv;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// Attaches <see cref="KidsPlaybackFilter"/> to Jellyfin's playback actions only, so every other request carries no
/// filter at all (research R2, R3), and <see cref="KidsLiveTvFilter"/> to those and to Jellyfin's live TV browse and
/// recording actions (feature 6, research R3). Runs once, while MVC builds its action list.
/// </summary>
/// <remarks>
/// An action is guarded when its controller and action names are in <see cref="GateActions.Expected"/>, or when it
/// carries Jellyfin's <c>ProducesFile</c> attribute (or a subclass) with a video, audio or HLS playlist content type,
/// outside <see cref="GateActions.ExcludedControllers"/>. The attribute is matched by name because <c>Jellyfin.Api</c>
/// is not a package plugins can reference; it catches a playback action renamed in a later 12.x.
/// </remarks>
public sealed class PlaybackGateConvention : IApplicationModelConvention
{
    private readonly GateCoverage _coverage;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackGateConvention"/> class.
    /// </summary>
    /// <param name="coverage">Where matches are recorded for the startup check.</param>
    public PlaybackGateConvention(GateCoverage coverage)
    {
        _coverage = coverage;
    }

    /// <inheritdoc />
    public void Apply(ApplicationModel application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var ownAssembly = typeof(PlaybackGateConvention).Assembly;
        foreach (var controller in application.Controllers)
        {
            if (controller.ControllerType.Assembly == ownAssembly)
            {
                continue;
            }

            foreach (var action in controller.Actions)
            {
                var kind = Match(controller.ControllerName, action);

                // Live TV (feature 6): every playback action, plus Jellyfin's live TV browse and recording actions
                var rule = LiveTvActions.RuleFor(controller.ControllerName, action.ActionName) ?? (kind is null ? null : LiveTvRule.Playback);
                if (rule is not null)
                {
                    action.Filters.Add(new ServiceFilterAttribute(typeof(KidsLiveTvFilter)) { IsReusable = true });
                    action.Properties[typeof(LiveTvRule)] = rule.Value;
                    if (controller.ControllerName == LiveTvActions.Controller)
                    {
                        _coverage.AddLiveTv(action.ActionName);
                    }
                }

                if (kind is null)
                {
                    continue;
                }

                action.Filters.Add(new ServiceFilterAttribute(typeof(KidsPlaybackFilter)) { IsReusable = true });
                action.Properties[typeof(GateKind)] = kind.Value;
                var byName = GateActions.Expected.Any(e => e.Controller == controller.ControllerName && e.Action == action.ActionName);
                _coverage.Add(controller.ControllerName, action.ActionName, kind.Value, byName);
            }
        }
    }

    private static GateKind? Match(string controllerName, ActionModel action)
    {
        foreach (var (controller, name, kind) in GateActions.Expected)
        {
            if (controller == controllerName && name == action.ActionName)
            {
                return kind;
            }
        }

        if (GateActions.ExcludedControllers.Contains(controllerName))
        {
            return null;
        }

        return action.Attributes.Any(IsMediaFileAttribute) ? GateKind.Deliver : null;
    }

    private static bool IsMediaFileAttribute(object attribute)
    {
        for (var type = attribute.GetType(); type is not null; type = type.BaseType)
        {
            if (type.Name != "ProducesFileAttribute")
            {
                continue;
            }

            if (type.GetProperty("ContentTypes", BindingFlags.Public | BindingFlags.Instance)?.GetValue(attribute) is not IEnumerable types)
            {
                return false;
            }

            return types.OfType<string>().Any(t =>
                t.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
                || t.Equals("application/x-mpegURL", StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }
}
