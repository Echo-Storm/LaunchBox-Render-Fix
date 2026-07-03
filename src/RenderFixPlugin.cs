using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LaunchBoxRenderFix
{
    /// <summary>
    /// Cuts LaunchBox/Big Box's idle GPU usage by capping animation frame rate and using
    /// cheaper image scaling, without disabling hardware rendering.
    /// See README.md for the full explanation of why this exists and what it trades off.
    /// </summary>
    public class RenderFixPlugin : ISystemEventsPlugin
    {
        private const int AnimationFrameRateCap = 10;
        private static bool _frameCapApplied;
        private static bool _scalingModeApplied;

        public void OnEventRaised(string eventType)
        {
            // Applied as early as possible (before any Timeline exists) - overriding a type's
            // default metadata has to happen before the type establishes its own metadata,
            // so PluginInitialized is the best shot at this.
            if (!_frameCapApplied && IsAnyStartupEvent(eventType))
            {
                try
                {
                    Timeline.DesiredFrameRateProperty.OverrideMetadata(
                        typeof(Timeline),
                        new FrameworkPropertyMetadata { DefaultValue = AnimationFrameRateCap });
                }
                catch (ArgumentException)
                {
                    // A Timeline was already created before this plugin ran (metadata is
                    // locked in as soon as that happens) - too late to cap it this session.
                }

                _frameCapApplied = true;
            }

            // Applied once the main window actually exists. This sets an inherited property
            // value on one instance (the window), it does NOT override BitmapScalingMode's
            // type metadata - that specific property's default is locked in inside
            // UIElement's own static constructor before any plugin runs, so OverrideMetadata
            // on it always throws. Setting the inherited value on the root window instead
            // cascades to every descendant that doesn't set its own value, which is
            // effectively everything, without touching that locked-in default at all.
            if (!_scalingModeApplied
                && (eventType == SystemEventTypes.LaunchBoxStartupCompleted
                    || eventType == SystemEventTypes.BigBoxStartupCompleted))
            {
                var mainWindow = Application.Current?.MainWindow;
                if (mainWindow != null)
                {
                    RenderOptions.SetBitmapScalingMode(mainWindow, BitmapScalingMode.LowQuality);
                    _scalingModeApplied = true;
                }
            }
        }

        private static bool IsAnyStartupEvent(string eventType)
        {
            return eventType == SystemEventTypes.PluginInitialized
                || eventType == SystemEventTypes.LaunchBoxStartupCompleted
                || eventType == SystemEventTypes.BigBoxStartupCompleted;
        }
    }
}
