using LabKom.Shared.Contracts;

namespace LabKom.Student.Desktop.Services.Capture;

public readonly record struct CapturePlan(
    int Width,
    int Height,
    int IntervalMilliseconds,
    int JpegQuality,
    int TargetFramesPerSecond,
    int AdaptationLevel);

/// <summary>
/// Kontrol adaptif konservatif: turun cepat saat upload/capture menumpuk dan
/// naik perlahan setelah beberapa frame sehat agar grid 40 PC tetap stabil.
/// </summary>
public sealed class AdaptiveStreamController
{
    private static readonly double[] Scale = [1.00, 0.85, 0.70, 0.55, 0.45];
    private static readonly double[] IntervalScale = [1.00, 1.25, 1.60, 2.20, 3.00];
    private static readonly int[] QualityReduction = [0, 5, 10, 15, 20];

    private readonly object _gate = new();
    private readonly Dictionary<CaptureProfile, AdaptationState> _states = new();

    public CapturePlan GetPlan(
        CaptureProfile profile,
        int baseWidth,
        int baseHeight,
        int baseIntervalMilliseconds,
        int baseJpegQuality)
    {
        lock (_gate)
        {
            var state = StateFor(profile);
            var level = state.Level;
            var width = Math.Max(320, (int)Math.Round(baseWidth * Scale[level]));
            var height = Math.Max(180, (int)Math.Round(baseHeight * Scale[level]));
            var interval = Math.Clamp(
                (int)Math.Round(baseIntervalMilliseconds * IntervalScale[level]),
                100,
                60_000);
            var quality = Math.Clamp(
                baseJpegQuality - QualityReduction[level],
                30,
                95);
            return new CapturePlan(
                width,
                height,
                interval,
                quality,
                Math.Clamp((int)Math.Round(1_000d / interval), 1, 60),
                level);
        }
    }

    public void Observe(
        CaptureProfile profile,
        int frameBytes,
        int captureMilliseconds,
        int sendMilliseconds,
        bool delivered,
        int maximumKilobitsPerSecond,
        CapturePlan plan)
    {
        lock (_gate)
        {
            var state = StateFor(profile);
            var estimatedKbps = frameBytes <= 0
                ? 0d
                : frameBytes * 8d / Math.Max(1, plan.IntervalMilliseconds);
            var budgetMs = plan.IntervalMilliseconds * 0.85;
            var pressure = !delivered
                           || sendMilliseconds >= budgetMs
                           || captureMilliseconds + sendMilliseconds >= plan.IntervalMilliseconds
                           || estimatedKbps > Math.Max(64, maximumKilobitsPerSecond);

            if (pressure)
            {
                state.PressureSamples++;
                state.HealthySamples = 0;
                if (state.PressureSamples >= 2 && state.Level < Scale.Length - 1)
                {
                    state.Level++;
                    state.PressureSamples = 0;
                }

                return;
            }

            state.PressureSamples = 0;
            state.HealthySamples++;
            if (state.HealthySamples >= 10 && state.Level > 0)
            {
                state.Level--;
                state.HealthySamples = 0;
            }
        }
    }

    public void Reset(CaptureProfile profile)
    {
        lock (_gate)
        {
            _states.Remove(profile);
        }
    }

    private AdaptationState StateFor(CaptureProfile profile)
    {
        if (_states.TryGetValue(profile, out var state)) return state;
        state = new AdaptationState();
        _states.Add(profile, state);
        return state;
    }

    private sealed class AdaptationState
    {
        public int Level;
        public int PressureSamples;
        public int HealthySamples;
    }
}
