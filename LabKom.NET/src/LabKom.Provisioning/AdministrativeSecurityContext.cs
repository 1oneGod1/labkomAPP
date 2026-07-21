using System.Security.Principal;
using LabKom.Shared.Security;

namespace LabKom.Provisioning;

internal static class AdministrativeSecurityContext
{
    public static int Execute(
        string action,
        string? target,
        string permission,
        Func<int> operation)
    {
        var actor = RequireLocalAdministrator();
        var journal = OpenJournal();
        journal.Append(
            actor.Sid,
            actor.Name,
            LabKomRole.Administrator,
            action,
            target,
            "authorized",
            permission);
        try
        {
            var result = operation();
            journal.Append(
                actor.Sid,
                actor.Name,
                LabKomRole.Administrator,
                action,
                target,
                "succeeded",
                $"exit:{result}");
            return result;
        }
        catch (Exception exception)
        {
            journal.Append(
                actor.Sid,
                actor.Name,
                LabKomRole.Administrator,
                action,
                target,
                "failed",
                exception.GetType().Name);
            throw;
        }
    }

    public static SecurityAuditJournal OpenJournal()
    {
        var provisioning = ProvisionedSecretStore.Read();
        return SecurityAuditJournal.OpenMachineJournal(provisioning.Secret);
    }

    public static AdministratorIdentity RequireLocalAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException(
                "Jalankan LabKom.Provisioning dari terminal Run as administrator.");

        return new AdministratorIdentity(
            identity.User?.Value
            ?? throw new InvalidOperationException("SID administrator tidak tersedia."),
            identity.Name ?? Environment.UserName);
    }

    internal sealed record AdministratorIdentity(string Sid, string Name);
}
