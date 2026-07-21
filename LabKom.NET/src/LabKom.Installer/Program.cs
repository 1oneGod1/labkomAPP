using System.Reflection;

namespace LabKom.Installer;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var component = Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .SingleOrDefault(attribute => attribute.Key == "InstallerComponent")
                ?.Value;
            if (component is not ("Student" or "Teacher"))
                throw new InvalidOperationException(
                    "Installer development tidak memiliki payload Student/Teacher. Gunakan scripts/build-release.ps1.");

            var options = InstallerOptions.Parse(component, args);
            var logger = new InstallerLogger(options.Silent);
            var engine = new InstallerEngine(logger);
            return options.Uninstall ? engine.Uninstall(options) : engine.Install(options);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Setup LabKom gagal: {exception.Message}");
            return 1;
        }
    }
}
