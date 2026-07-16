using LabKom.Shared.Contracts;

namespace LabKom.Teacher.Services;

/// <summary>
/// Desired Attention state retained across Desktop reconnects. State is scoped
/// to the running Teacher process; durable lesson-state persistence is separate.
/// </summary>
public sealed class AttentionStateStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, AttentionDesiredState> _perPc =
        new(StringComparer.OrdinalIgnoreCase);
    private AttentionDesiredState? _global;

    public void Apply(AttentionCommand command)
    {
        var desired = new AttentionDesiredState(command.Enabled, command.Message);
        lock (_sync)
        {
            if (command.TargetPcName is null)
            {
                _global = desired;
                _perPc.Clear();
            }
            else
            {
                _perPc[command.TargetPcName] = desired;
            }
        }
    }

    public AttentionCommand? BuildReplayFor(string pcName)
    {
        AttentionDesiredState? desired;
        lock (_sync)
        {
            desired = _perPc.TryGetValue(pcName, out var individual)
                ? individual
                : _global;
        }

        return desired is { Enabled: true }
            ? AttentionCommand.For(pcName, true, desired.Message)
            : null;
    }

    private sealed record AttentionDesiredState(bool Enabled, string Message);
}
