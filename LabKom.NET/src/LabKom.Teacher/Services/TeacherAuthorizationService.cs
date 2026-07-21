using System.IO;
using System.Security.Principal;
using LabKom.Shared.Security;

namespace LabKom.Teacher.Services;

/// <summary>
/// Resolves the current Windows identity to a LabKom role and fails closed when
/// authorization or tamper-evident audit persistence fails.
/// </summary>
public sealed class TeacherAuthorizationService
{
    private readonly SecurityAuditJournal _audit;

    public TeacherAuthorizationService(SecurityAuditJournal audit)
    {
        _audit = audit;
    }

    public LabKomRole CurrentRole => ResolveCurrent().Role;

    public async Task ExecuteAsync(
        TeacherPermission permission,
        string action,
        string? target,
        Func<Task> operation)
    {
        var actor = DemandCore(permission, action, target);
        try
        {
            await operation();
            Audit(actor, action, target, "succeeded");
        }
        catch (Exception exception)
        {
            Audit(actor, action, target, "failed", exception.GetType().Name);
            throw;
        }
    }

    public async Task<T> ExecuteAsync<T>(
        TeacherPermission permission,
        string action,
        string? target,
        Func<Task<T>> operation)
    {
        var actor = DemandCore(permission, action, target);
        try
        {
            var result = await operation();
            Audit(actor, action, target, "succeeded");
            return result;
        }
        catch (Exception exception)
        {
            Audit(actor, action, target, "failed", exception.GetType().Name);
            throw;
        }
    }

    public T Execute<T>(
        TeacherPermission permission,
        string action,
        string? target,
        Func<T> operation)
    {
        var actor = DemandCore(permission, action, target);
        try
        {
            var result = operation();
            Audit(actor, action, target, "succeeded");
            return result;
        }
        catch (Exception exception)
        {
            Audit(actor, action, target, "failed", exception.GetType().Name);
            throw;
        }
    }

    public void Demand(
        TeacherPermission permission,
        string action,
        string? target)
    {
        var actor = DemandCore(permission, action, target);
        Audit(actor, action, target, "authorized", permission.ToString());
    }

    public void RecordSystemEvent(
        string action,
        string? target,
        string outcome,
        string? detail = null)
    {
        _audit.Append(
            "S-1-5-18",
            "LabKom Teacher Hub",
            LabKomRole.Administrator,
            action,
            target,
            outcome,
            detail);
    }

    private ActorContext DemandCore(
        TeacherPermission permission,
        string action,
        string? target)
    {
        var actor = ResolveCurrent();
        if (!_audit.IntegrityValid)
            throw new InvalidDataException(
                "Security audit rusak; perbaiki audit sebelum menjalankan kontrol kelas.");
        if (RbacPolicy.IsAllowed(actor.Role, permission)) return actor;

        Audit(actor, action, target, "denied", permission.ToString());
        throw new UnauthorizedAccessException(
            $"Role {actor.Role} tidak memiliki izin {permission}.");
    }

    private void Audit(
        ActorContext actor,
        string action,
        string? target,
        string outcome,
        string? detail = null) =>
        _audit.Append(
            actor.Sid,
            actor.Name,
            actor.Role,
            action,
            target,
            outcome,
            detail);

    private static ActorContext ResolveCurrent()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value
                  ?? throw new InvalidOperationException("SID pengguna Windows tidak tersedia.");
        var principal = new WindowsPrincipal(identity);
        var role = principal.IsInRole(WindowsBuiltInRole.Administrator)
            ? LabKomRole.Administrator
            : RbacAssignmentStore.FindRole(sid) ?? LabKomRole.Observer;
        return new ActorContext(sid, identity.Name ?? Environment.UserName, role);
    }

    private sealed record ActorContext(string Sid, string Name, LabKomRole Role);
}
