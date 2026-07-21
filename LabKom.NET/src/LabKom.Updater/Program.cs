namespace LabKom.Updater;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = UpdateOptions.Parse(args);
            var logger = new UpdateLogger(options.Component);
            var engine = new UpdateEngine(logger);
            return options.Command switch
            {
                UpdateCommand.Check => await engine.CheckAndApplyAsync(options),
                UpdateCommand.Rollback => await engine.RollbackAsync(options),
                _ => throw new ArgumentOutOfRangeException(nameof(options.Command)),
            };
        }
        catch (CommandLineException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(UpdateOptions.Usage);
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Update LabKom gagal: {exception.Message}");
            return 1;
        }
    }
}
