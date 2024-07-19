using HentaiMigrator.Enums;
using HentaiMigrator.Interfaces;
using HentaiMigrator.Sites.NHentai;
using HentaiMigrator.Sites.EHentai;
using HentaiMigrator.Exceptions;
using HentaiMigrator.Utils;
using Serilog;

namespace HentaiMigrator.Migrators;

public class Migrator
{
    private readonly ISite MigratorFrom;
    private readonly Site SiteFrom;
    private readonly ISite MigratorTo;
    private readonly Site SiteTo;

    public Migrator(Site from, Site to, string dataDirectory)
    {
        SiteFrom = from;
        SiteTo = to;

        bool continueFromSavedData;
        bool reuseAuthCredentials;
        if
        (
            Directory.GetFiles(dataDirectory)
                .Where(x => Path.GetFileName(x) != "authenticationCredentials.json")
                .Any(x => Path.GetFileName(x) != "log.txt")
        )
        {
            Log.Information("Continue with previously saved data? [y/n]");
            continueFromSavedData = Input.GetYesNoInput();

            Log.Information("Re-use previously saved User-Agent and Cookies? [y/n]");
            reuseAuthCredentials = Input.GetYesNoInput();
        } else
        {
            continueFromSavedData = false;
            reuseAuthCredentials = false;
        }
        
        WriteOnUpdateDictionary<string, string> authenticationCredentials = new(Path.Combine(dataDirectory, "authenticationCredentials.json"), initializeFromFile: reuseAuthCredentials);

        if (!authenticationCredentials.ContainsKey("userAgent"))
        {
            authenticationCredentials["userAgent"] = Input.GetUserAgent();
        }

        MigratorFrom = SiteSelector(from, dataDirectory, authenticationCredentials, Direction.From, continueFromSavedData);
        MigratorTo = SiteSelector(to, dataDirectory, authenticationCredentials, Direction.To, continueFromSavedData);
    }



    public void Migrate()
    {
        try
        {
            WriteOnUpdateDictionary<string, Dictionary<string, List<string>>> favoritesExported = MigratorFrom.Export();
            MigratorTo.Import(favoritesExported, SiteFrom);
            Log.Information("Finished.");
        }
        catch (IPBannedException ex)
        {
            Log.Error("{Site} IP address banned.\n{Message}", ex.Site, ex.Message);
            Log.Debug("Content:\n{Content}", ex.Response.Content.ReadAsStringAsync().Result);
        }
    }


    private static ISite SiteSelector(Site site, string dataDirectory, WriteOnUpdateDictionary<string, string> authenticationCredentials, Direction direction, bool continueFromSavedData)
    {
        switch (site)
        {
            case Site.NHentai: return new NHentai(dataDirectory, authenticationCredentials, direction, continueFromSavedData);
            case Site.EHentai: return new EHentai(dataDirectory, authenticationCredentials, direction, continueFromSavedData);
            default: throw new ArgumentException("Invalid site", site.ToString());
        }
    }
}
