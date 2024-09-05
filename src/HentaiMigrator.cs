using HentaiMigrator.Enums;
using HentaiMigrator.Logger;
using HentaiMigrator.Migrators;
using HentaiMigrator.Utils;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;



namespace HentaiMigrator;
public static class HentaiMigrator
{
    static readonly string workingDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
    static readonly string dataDirectory = Path.Combine(workingDirectory, "data");

    static void Main(string[] args)
    {
        if (!Directory.Exists(dataDirectory))
        {
            Directory.CreateDirectory(Path.Combine(workingDirectory, "data"));
        }


        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.WithCaller()
            .WriteTo.File
            (
                Path.Combine(dataDirectory, "log.txt"),
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3} {Namespace}.{Class}.{Method}()] {Message:lj}{NewLine}{Exception}"
            )
            .WriteTo.Console
            (
                outputTemplate: "[{Class}] {Message:l}{NewLine}",
                restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information,
                theme: ConsoleTheme.None
            )
            .CreateLogger();


        try
        {
            Site siteFrom = Input.GetSiteInput(Direction.From);
            Site siteTo = Input.GetSiteInput(Direction.To);

            while (siteFrom == siteTo)
            {
                Log.Warning("Invalid input. Sites must be different.");

                siteFrom = Input.GetSiteInput(Direction.From);
                siteTo = Input.GetSiteInput(Direction.To);
            }
            

            Migrator migrator = new(siteFrom, siteTo, dataDirectory);
            migrator.Migrate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unknown exception occurred.");
        }
        finally
        {
            Log.CloseAndFlush();
            Console.WriteLine("Press Enter to exit...");
            Console.ReadLine();
        }
    }
}
