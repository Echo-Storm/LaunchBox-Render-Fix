using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LaunchBoxGpuToCpuFix
{
    /// <summary>
    /// Moves LaunchBox/Big Box's idle UI rendering cost off the GPU and onto the CPU.
    /// See README.md for the full explanation of why this exists and what it trades off.
    /// </summary>
    public class GpuToCpuFixPlugin : ISystemEventsPlugin
    {
        private const int AnimationFrameRateCap = 30;
        private static bool _applied;

        public void OnEventRaised(string eventType)
        {
            if (_applied)
            {
                return;
            }

            var isStartupEvent = eventType == SystemEventTypes.PluginInitialized
                || eventType == SystemEventTypes.LaunchBoxStartupCompleted
                || eventType == SystemEventTypes.BigBoxStartupCompleted;

            if (!isStartupEvent)
            {
                return;
            }

            // Forces WPF to composite entirely on the CPU instead of the GPU for this process.
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            // Caps how often animations (hover glow, fades, scroll momentum) get re-rendered,
            // cutting the CPU cost of software rasterization since WPF's compositor is
            // single-threaded regardless of render mode.
            try
            {
                Timeline.DesiredFrameRateProperty.OverrideMetadata(
                    typeof(Timeline),
                    new FrameworkPropertyMetadata { DefaultValue = AnimationFrameRateCap });
            }
            catch (ArgumentException)
            {
                // A Timeline was already created before this plugin ran (metadata is locked
                // in as soon as that happens) - too late to cap it this session, but the
                // software render mode change above still applies.
            }

            _applied = true;
        }
    }
}
