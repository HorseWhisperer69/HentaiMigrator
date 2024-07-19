using HentaiMigrator.Enums;
using Serilog;

namespace HentaiMigrator.Utils;

public static class Input
{
    public static string GetUserAgent()
    {
        string? userInput;

        Log.Information("Enter your user agent:");
        while(true)
        {
            userInput = Console.ReadLine();
            if (!string.IsNullOrEmpty(userInput))
            {
                try
                {
                    HttpClient client = new HttpClient();
                    client.DefaultRequestHeaders.Add("User-Agent", userInput);
                    return userInput;
                }
                catch(Exception ex)
                {
                    // Exception on invalid User-Agent
                    Log.Error(ex, ex.Message);
                } 
            }
            Log.Warning("Invalid input. Please enter a valid user agent.");
        }
    }


    public static double GetDelayInput(double defaultDelaySeconds, Site site)
    {
        string? userInput;

        Log.Information("Enter {Site} request delay in seconds, or leave blank for default ({DefaultDelaySeconds}s):", site, defaultDelaySeconds);
        while (true)
        {
            userInput = Console.ReadLine();

            if (userInput != null)
            {
                if (userInput.Trim() == "") return defaultDelaySeconds;

                else if (double.TryParse(userInput.Trim(), out double parsedDelay))
                {
                    if (parsedDelay > 0)
                    {
                        return parsedDelay;
                    }
                    else
                    {
                        Log.Warning("Invalid input. Please enter a positive number.");
                    }
                }
            }
        }
    }


    public static string GetCookieInput(Site site)
    {
        string? userInput;

        Log.Information("Enter {Site} cookie:", site);
        while (true)
        {
            userInput = Console.ReadLine();

            if (!string.IsNullOrEmpty(userInput))
            {
                if (site == Site.NHentai &&
                    userInput.Trim().Contains("csrftoken=") &&
                    userInput.Trim().Contains("sessionid="))
                    {
                        return userInput.Trim();
                    }
                
                if (site == Site.EHentai &&
                    userInput.Trim().Contains("ipb_member_id=") &&
                    userInput.Trim().Contains("ipb_pass_hash="))
                    {
                        return userInput.Trim();
                    }
            }

            Log.Warning("Invalid input. Please enter a valid cookie.");
        }
    }


    public static Site GetSiteInput(Enum direction)
    {
        string? userInput;

        Log.Information("Enter site to transfer {Direction}:", direction);
        foreach (Site site in Enum.GetValues(typeof(Site)))
        {
            Log.Information("{Index}. {Site}", (int)site + 1, site);
        }

        while(true)
        {
            userInput = Console.ReadLine();
            if (!string.IsNullOrEmpty(userInput))
            {
                switch (userInput.Trim().ToLower())
                {
                    case "1":
                    case "nhentai":
                        return Site.NHentai;
                    
                    case "2":
                    case "ehentai":
                        return Site.EHentai;
                }
            }
        }
    }


    public static bool GetYesNoInput()
    {
        string? userInput;

        while(true)
        {
            userInput = Console.ReadLine();
            if (!string.IsNullOrEmpty(userInput))
            {
                switch (userInput.Trim().ToLower())
                {
                    case "yes":
                    case "y":
                        return true;
                    
                    case "no":
                    case "n":
                        return false;
                }
            }
        }
    }


    public static int? GetEHentaiSlotInput()
    {
        string? userInput;

        while (true)
        {
            userInput = Console.ReadLine();
            if (userInput != null)
            {
                if (userInput == "")
                {
                    return null;
                }
                else if (int.TryParse(userInput, out int slot) && slot >= 0 && slot <= 9)
                {
                    return slot;
                }
                else
                {
                    Log.Warning("Invalid input. Please enter a number between 0 and 9.");
                }
            }
        }
    }
}
