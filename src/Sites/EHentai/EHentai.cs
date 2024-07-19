using System.Diagnostics;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using HtmlAgilityPack;
using ShellProgressBar;

using HentaiMigrator.Enums;
using HentaiMigrator.Utils;
using HentaiMigrator.Interfaces;
using HentaiMigrator.Exceptions;
using Serilog;

namespace HentaiMigrator.Sites.EHentai;

public class EHentai: ISite
{
    public readonly double DefaultDelaySeconds = 3;
    public readonly int maxLeniency = 4;
    private readonly HttpClient HttpClient;
    private readonly Stopwatch RequestStopwatch;
    public double RequestDelaySeconds { get; private set; }
    public int RequestCount { get; private set; }
    private readonly Site WebSite = Site.EHentai;
    public string BaseUrl { get; private set; }
    public readonly string DataDirectory;
    private readonly bool ContinueFromSavedData;

    public EHentai(string dataDirectory, WriteOnUpdateDictionary<string, string> authenticationCredentials, Direction direction, bool continueFromSavedData)
    {
        DataDirectory = dataDirectory;
        ContinueFromSavedData = continueFromSavedData;
        RequestCount = 0;
        RequestDelaySeconds = Input.GetDelayInput(DefaultDelaySeconds, WebSite);
        Log.Debug("Request delay set to {Delay} seconds.", RequestDelaySeconds);
        RequestStopwatch = new Stopwatch();
        RequestStopwatch.Start();
        HttpClient = new HttpClient();
        HttpClient.DefaultRequestHeaders.Add("User-Agent", authenticationCredentials["userAgent"]);
        if (authenticationCredentials.ContainsKey("ehentaiCookie"))
        {
            HttpClient.DefaultRequestHeaders.Add("Cookie", authenticationCredentials["ehentaiCookie"]);
        } else
        {
            authenticationCredentials["ehentaiCookie"] = Input.GetCookieInput(WebSite);
            HttpClient.DefaultRequestHeaders.Add("Cookie", authenticationCredentials["ehentaiCookie"]);
        }

        bool authenticated;
        while (true)
        {
            try
            {
                authenticated = Authenticate(direction);
            }
            catch (AuthenticationException ex)
            {
                Log.Error(ex, ex.Message);
                authenticated = false;
            }

            if (authenticated) break;
            else
            {
                authenticationCredentials["userAgent"] = Input.GetUserAgent();
                authenticationCredentials["ehentaiCookie"] = Input.GetCookieInput(WebSite);
                HttpClient.DefaultRequestHeaders.Add("User-Agent", authenticationCredentials["userAgent"]);
                HttpClient.DefaultRequestHeaders.Add("Cookie", authenticationCredentials["ehentaiCookie"]);
            }
        }
    }


    private List<(string, string)> GetFavoritesIDs(int? slot)
    {
        Log.Information("Getting favorites IDs...");

        List<(string, string)> favoritesIDs = new();

        HttpResponseMessage response;
        if (slot == null)
        {
            response = GetRequest($"{BaseUrl}/favorites.php?inline_set=dm_m");
        }
        else
        {
            response = GetRequest($"{BaseUrl}/favorites.php?favcat={slot}&inline_set=dm_m");
        }

        string html = response.Content.ReadAsStringAsync().Result;
        if (html.Contains("Your IP address has been temporarily banned for excessive pageloads."))
        {
            throw new IPBannedException(Site.EHentai, response);
        }
        HtmlDocument htmlDoc = new();
        htmlDoc.LoadHtml(html);
        HtmlNodeCollection favoriteSlotNodes = htmlDoc.DocumentNode.SelectNodes("/html/body/div[2]/div[1]/div/div/parent::*");

        int numberOfFavorites = 0;
        foreach (HtmlNode slotNode in favoriteSlotNodes)
        {
            if (slot == null || slotNode.ChildNodes[5].InnerText.EndsWith(slot.ToString()))
            {
                numberOfFavorites += int.Parse(slotNode.ChildNodes[1].InnerText);
            }
        }
        if (numberOfFavorites == 0)
        {
            return favoritesIDs;
        }

        int pageCount = numberOfFavorites / 50;
        if (numberOfFavorites % 50 > 0) pageCount += 1;

        int numberParsed = 0;
        using (ProgressBar pBar = new(numberOfFavorites, $"{numberParsed}/{numberOfFavorites}"))
        {
            int currPage = 1;
            while (true)
            {
                // https://stackoverflow.com/questions/18241029/why-does-my-xpath-query-scraping-html-tables-only-work-in-firebug-but-not-the
                HtmlNode[] doujinshiNodeArray = htmlDoc.DocumentNode.SelectNodes("/html/body/div[2]/form/table/tr/td[4]/a").ToArray();
                foreach (HtmlNode doujinshiNode in doujinshiNodeArray)
                {
                    string ID = doujinshiNode.GetAttributeValue("href", "").Split("/")[4];
                    string Token = doujinshiNode.GetAttributeValue("href", "").Split("/")[5];
                    favoritesIDs.Add((ID, Token));
                    numberParsed++;
                    pBar.Tick($"{numberParsed}/{numberOfFavorites}");
                }

                if (currPage == pageCount) break;
                else
                {
                    currPage++;
                    string nextPage = htmlDoc.DocumentNode.SelectSingleNode("//*[@id=\"dnext\"]").GetAttributeValue("href", "").Replace("&amp;", "&");
                    response = GetRequest($"{nextPage}&inline_set=dm_m");
                    html = response.Content.ReadAsStringAsync().Result;
                    htmlDoc = new HtmlDocument();
                    htmlDoc.LoadHtml(html);
                }
            }
        }

        return favoritesIDs;
    }

    
    public WriteOnUpdateDictionary<string, Dictionary<string, List<string>>> Export()
    {
        WriteOnUpdateDictionary<string, Dictionary<string, List<string>>> exportedFavorites = new(Path.Combine(DataDirectory, "exportedEHentaiFavorites.json"), initializeFromFile: ContinueFromSavedData);
        // return exportedFavorites;

        Log.Information("Enter slot to export from, or leave blank to export from all ([0-9], default all):");

        List<(string ID, string Token)> favoritesIDs = GetFavoritesIDs(Input.GetEHentaiSlotInput());
        if (favoritesIDs.Count == 0)
        {
            Log.Information("No favorites found");
            return new WriteOnUpdateDictionary<string, Dictionary<string, List<string>>>(Path.Combine(DataDirectory, "exportedEHentaiFavorites.json"), initializeFromFile: false);
        }

        Log.Information("Getting favorites metadata...");

        int numberOfFavorites = favoritesIDs.Count;
        int numberParsed = 0;
        using (ProgressBar pBar = new(numberOfFavorites, $"{numberParsed}/{numberOfFavorites}"))
        {
            List<(string, string)> favoritesIDsChunked = new();
            for (int i = 0; i < numberOfFavorites; i++)
            {
                if(exportedFavorites.ContainsKey(favoritesIDs[i].ID))
                {
                    numberParsed++;
                    pBar.Tick($"{numberParsed}/{numberOfFavorites}");
                    continue;
                } else favoritesIDsChunked.Add(favoritesIDs[i]);

                if (favoritesIDsChunked.Count == 25 || i == numberOfFavorites - 1)
                {
                    Dictionary<string, Dictionary<string, List<string>>> doujinshiInfoBatch = GetDoujinshiInfoBatch(favoritesIDsChunked);
                    foreach ((string iDoujinshiID, Dictionary<string, List<string>> iDoujinshiInfo) in doujinshiInfoBatch)
                    {
                        exportedFavorites[iDoujinshiID] = iDoujinshiInfo;
                        numberParsed++;
                        pBar.Tick($"{numberParsed}/{numberOfFavorites}");
                    }
                    favoritesIDsChunked.Clear();
                }
            }
        }

        foreach (string ehentaiID in exportedFavorites.Select(x => x.Key))
        {
            if (!favoritesIDs.Select(x => x.ID).Contains(ehentaiID))
            {
                exportedFavorites.Remove(ehentaiID);
            }
        }

        return exportedFavorites;
    }


    private Dictionary<string, Dictionary<string, List<string>>> GetDoujinshiInfoBatch(List<(string, string)> doujinshiIDBatch)
    {
        Dictionary<string, Dictionary<string, List<string>>> doujinshiInfo = new();
        
        List<List<string>> gidList = new();
        foreach ((string ID, string Token) in doujinshiIDBatch)
        {
            gidList.Add([ID, Token]);
        }
        Dictionary<string, object> data = new()
        {
            { "method", "gdata" },
            { "gidlist", gidList},
            { "namespace", 1 }
        };
        StringContent content = new(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
        HttpResponseMessage response = PostRequest("https://api.e-hentai.org/api.php", content);
        JsonElement json = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result).RootElement;

        foreach (JsonElement doujinshi in json.GetProperty("gmetadata").EnumerateArray())
        {
            string ID = doujinshi.GetProperty("gid").ToString();

            doujinshiInfo[ID] = new Dictionary<string, List<string>>();
            doujinshiInfo[ID]["doujinshiToken"] = new List<string>() {doujinshi.GetProperty("token").ToString()};
            // ' is encoded as &#039;
            // & is encoded as &amp;
            doujinshiInfo[ID]["titleEN"] = new List<string>() {doujinshi.GetProperty("title").ToString().Replace("&#039;", "'").Replace("&amp;", "&")};
            doujinshiInfo[ID]["titleJP"] = new List<string>() {doujinshi.GetProperty("title_jpn").ToString().Replace("&#039;", "'").Replace("&amp;", "&")};
            doujinshiInfo[ID]["category"] = new List<string>() {doujinshi.GetProperty("category").ToString()};
            doujinshiInfo[ID]["pages"] = new List<string>() {doujinshi.GetProperty("filecount").ToString()};
            doujinshiInfo[ID]["parody"] = new List<string>();
            doujinshiInfo[ID]["tag"] = new List<string>();
            doujinshiInfo[ID]["artist"] = new List<string>();
            doujinshiInfo[ID]["group"] = new List<string>();
            doujinshiInfo[ID]["language"] = new List<string>();
            doujinshiInfo[ID]["character"] = new List<string>();

            foreach (JsonElement tag in doujinshi.GetProperty("tags").EnumerateArray())
            {
                string[] tagSplit = tag.ToString().Split(":");
                if (tagSplit.Length != 2) continue;
                string tagNamespace = tagSplit[0];
                string tagName = tagSplit[1];
                if ((new string[] { "parody", "artist", "group", "language", "character" }).Contains(tagNamespace))
                {
                    doujinshiInfo[ID][tagNamespace].Add(tagName);
                }
                else doujinshiInfo[ID]["tag"].Add(tagName);
            }
        }

        return doujinshiInfo;
    }


    public void Import(WriteOnUpdateDictionary<string, Dictionary<string, List<string>>> exportedFavorites, Site siteFrom)
    {
        switch (siteFrom)
        {
            case Site.NHentai:
                WriteOnUpdateDictionary<string, (string, string)> convertedFavorites = null;

                for (int leniency = 0; leniency <= maxLeniency; leniency++)
                {
                    Log.Information("Converting NHentai favorites to EHentai at leniency level {Leniency}...", leniency);
                    convertedFavorites = ConvertNHentaiToEHentai(exportedFavorites, leniency);

                    if (exportedFavorites.Count > convertedFavorites.Count) break;
                    if (leniency < maxLeniency)
                    {
                        Log.Information("Try to convert additional entries using higher leniency? [y/n]:");
                        if (Input.GetYesNoInput())
                        {
                            Log.Debug("Proceeding with leniency level {Leniency}...", leniency + 1);
                        }
                        else break;
                    }
                }

                AddToFavorites(convertedFavorites);
                break;
        }
    }


    private void AddToFavorites(WriteOnUpdateDictionary<string, (string, string)> convertedFavorites)
    {
        int? slot;
        Log.Information("Enter slot to import into: [0-9]");
        do
        {
            slot = Input.GetEHentaiSlotInput();
        } while (slot == null);

        List<(string, string)> favoritesIDs = GetFavoritesIDs(slot);
        if (favoritesIDs.Count == 0) Log.Information("No favorites found");

        Log.Information("Adding favorites...");
        
        IEnumerable<(string, string)> deduplicatedConvertedFavorites = convertedFavorites
            .Select(x => x.Value)
            .GroupBy(x => x)
            .Select(x => x.Key);
        
        int numberProcessed = 0;
        using (ProgressBar pBar = new(deduplicatedConvertedFavorites.Count(), $"{numberProcessed}/{deduplicatedConvertedFavorites.Count()}"))
        foreach ((string ID, string Token) in deduplicatedConvertedFavorites)
        {
            if (favoritesIDs.Contains((ID, Token)))
            {
                numberProcessed++;
                pBar.Tick($"{numberProcessed}/{deduplicatedConvertedFavorites.Count()}");
                continue;
            }

            Dictionary<string, string> data = new()
            {
                {"favcat", slot.ToString()},
                {"apply", "Add to Favorites"},
                {"favnote", ""},
                {"update", "1"}
            };
            string url = $"https://e-hentai.org/gallerypopups.php?gid={ID}&t={Token}&act=addfav";
            var _data = new FormUrlEncodedContent(data);

            double originalDelaySeconds = RequestDelaySeconds;
            int retryCount = 0;
            HttpResponseMessage response;
            do
            {
                response = PostRequest(url, _data);
                string html = response.Content.ReadAsStringAsync().Result;
                RequestDelaySeconds += 1;
                retryCount++;
                if (html.Contains("Your IP address has been temporarily banned for excessive pageloads.") || retryCount > 10)
                {
                    Log.Debug("Failed to add favorite {Favorite} after {RetryCount} retries", convertedFavorites.FirstOrDefault(x => x.Value == (ID, Token)), retryCount);
                    throw new IPBannedException(Site.EHentai, response);
                }
            } while (response.StatusCode != System.Net.HttpStatusCode.OK);

            numberProcessed++;
            pBar.Tick($"{numberProcessed}/{deduplicatedConvertedFavorites.Count()}");
            RequestDelaySeconds = originalDelaySeconds;
        }
    }


    public WriteOnUpdateDictionary<string, (string, string)> ConvertNHentaiToEHentai(WriteOnUpdateDictionary<string, Dictionary<string, List<string>>> parsedNHentaiFavorites, int leniency)
    {
        WriteOnUpdateDictionary<string, (string, string)> convertedNHentaiToEHentaiFavorites;
        WriteOnUpdateDictionary<string, Dictionary<string, string>> convertedNHentaiToEHentaiProgress;
        if (leniency == 0)
        {
            convertedNHentaiToEHentaiFavorites = new(Path.Combine(DataDirectory, "convertedNHentaiToEHentaiFavorites.json"), initializeFromFile: ContinueFromSavedData);
            convertedNHentaiToEHentaiProgress = new(Path.Combine(DataDirectory, "convertedNHentaiToEHentaiProgress.json"), initializeFromFile: ContinueFromSavedData);
        }
        else
        {
            convertedNHentaiToEHentaiFavorites = new(Path.Combine(DataDirectory, "convertedNHentaiToEHentaiFavorites.json"));
            convertedNHentaiToEHentaiProgress = new(Path.Combine(DataDirectory, "convertedNHentaiToEHentaiProgress.json"));
        }

        int numberProcessed = 0;

        using (ProgressBar pBar = new(parsedNHentaiFavorites.Count, $"{numberProcessed}/{parsedNHentaiFavorites.Count}"))
        foreach ((string nhentaiID, Dictionary<string, List<string>> nhentaiInfo) in parsedNHentaiFavorites)
        {
            if
            (
                convertedNHentaiToEHentaiProgress.ContainsKey(nhentaiID)
                &&
                    (convertedNHentaiToEHentaiProgress[nhentaiID]["converted"]  == "true"
                    ||
                    int.Parse(convertedNHentaiToEHentaiProgress[nhentaiID]["leniency"]) >= leniency)
            )
            {
                numberProcessed++;
                pBar.Tick($"{numberProcessed}/{parsedNHentaiFavorites.Count}");
                continue;
            }
            else
            {
                convertedNHentaiToEHentaiProgress[nhentaiID] = new()
                {
                    {"converted", "false"},
                    {"leniency", leniency.ToString()}
                };
            }


            string query = $"{BaseUrl}/?f_search=";
            switch (leniency)
            {
                case 0:
                    query += $"gid:\"{nhentaiInfo["mediaID"][0]}\"$&";
                    break;
                case 1:
                case 2:
                    query += $"title:\"{nhentaiInfo["titleEN"][0]}\"$&";
                    if (nhentaiInfo["language"].Count > 0)
                    {
                        query += $"language:\"{nhentaiInfo["language"][0]}\"&";
                    }
                    break;
                case 3:
                case 4:
                    if (nhentaiInfo["artist"].Count > 0)
                    {
                        query += $"artist:\"{nhentaiInfo["artist"][0]}\"&";
                    }
                    if (nhentaiInfo["artist"].Count > 0)
                    {
                        query += $"language:\"{nhentaiInfo["language"][0]}\"&";
                    }
                    break;
            }
            query += "inline_set=dm_m";

            HttpResponseMessage response = GetRequest(query);
            string html = response.Content.ReadAsStringAsync().Result;
            if (html.Contains("Your IP address has been temporarily banned for excessive pageloads."))
            {
                throw new IPBannedException(Site.EHentai, response);
            }
            HtmlDocument htmlDoc = new();
            htmlDoc.LoadHtml(html);
            // https://stackoverflow.com/questions/18241029/why-does-my-xpath-query-scraping-html-tables-only-work-in-firebug-but-not-the
            HtmlNodeCollection matchedFavorites = htmlDoc.DocumentNode.SelectNodes("/html/body/div[2]/div[2]/table/tr[2]/td[4]/a");

            if (matchedFavorites == null)
            {
                numberProcessed++;
                pBar.Tick($"{numberProcessed}/{parsedNHentaiFavorites.Count}");
                Log.Debug("Failed to convert NHentai ID {NHentaiID} with metadata {NHentaiInfo}", nhentaiID, nhentaiInfo);
                continue;
            }
            
            string? doujinshiID = null;
            string? doujinshiToken = null;
            foreach (HtmlNode matchedFavorite in matchedFavorites)
            {
                switch (leniency)
                {
                    case 0:
                    case 1:
                    case 3:
                        if (matchedFavorite.GetAttributeValue("href", "").Split("/")[4] == nhentaiInfo["mediaID"][0])
                        {
                            doujinshiID = matchedFavorite.GetAttributeValue("href", "").Split("/")[4];
                            doujinshiToken = matchedFavorite.GetAttributeValue("href", "").Split("/")[5];
                        }
                        break;
                    case 2:
                    case 4:
                        if (matchedFavorite.GetAttributeValue("href", "").Split("/")[4] == nhentaiInfo["mediaID"][0] ||
                            matchedFavorite.FirstChild.InnerText == nhentaiInfo["titleEN"][0])
                        {
                            doujinshiID = matchedFavorite.GetAttributeValue("href", "").Split("/")[4];
                            doujinshiToken = matchedFavorite.GetAttributeValue("href", "").Split("/")[5];
                        }
                        break;
                }
                if (doujinshiID != null || doujinshiToken != null) break;
            }
            
            if (doujinshiID == null || doujinshiToken == null)
            {
                numberProcessed++;
                pBar.Tick($"{numberProcessed}/{parsedNHentaiFavorites.Count}");
                Log.Debug("Failed to convert NHentai ID {NHentaiID} with metadata {NHentaiInfo}", nhentaiID, nhentaiInfo);
                continue;
            }
            
            convertedNHentaiToEHentaiProgress[nhentaiID]["converted"] = "true";
            convertedNHentaiToEHentaiProgress.WriteToFile();

            convertedNHentaiToEHentaiFavorites[nhentaiID] = (doujinshiID, doujinshiToken);
            numberProcessed++;
            pBar.Tick($"{numberProcessed}/{parsedNHentaiFavorites.Count}");
        }

        ConvertEHentaiToNHentaiProcessFailed(parsedNHentaiFavorites, convertedNHentaiToEHentaiProgress);
        ConvertEHentaiToNHentaiProcessDuplicates(convertedNHentaiToEHentaiFavorites);

        return convertedNHentaiToEHentaiFavorites;
    }


    private void ConvertEHentaiToNHentaiProcessFailed(WriteOnUpdateDictionary<string, Dictionary<string, List<string>>> parsedNHentaiFavorites, WriteOnUpdateDictionary<string, Dictionary<string, string>> convertedNHentaiToEHentaiProgress)
    {
        if (convertedNHentaiToEHentaiProgress.Values.Any(x => x["converted"] == "false"))
        {
            Log.Information("Failed to convert {FailedToConvertCount} entries", convertedNHentaiToEHentaiProgress.Values.Count(x => x["converted"] == "false"));
            string failedToConvertPath = Path.Combine(DataDirectory, "failedToConvertNHentaiToEHentaiFavorites.txt");
            string failedToConvertText = "";
            foreach ((string iNhentaiID, Dictionary<string, string> iProgress) in convertedNHentaiToEHentaiProgress)
            {
                if (iProgress["converted"] == "false")
                {
                    failedToConvertText += $"https://nhentai.org/g/{iNhentaiID}/: {parsedNHentaiFavorites[iNhentaiID]["titleEN"][0]}\n";
                }
            }
            File.WriteAllText(failedToConvertPath, failedToConvertText);
            Log.Information("Written to file {FailedToConvertPath}", failedToConvertPath);
        }
    }


    private void ConvertEHentaiToNHentaiProcessDuplicates(WriteOnUpdateDictionary<string, (string, string)> convertedNHentaiToEHentaiFavorites)
    {
        IEnumerable<IGrouping<(string, string), KeyValuePair<string, (string, string)>>> duplicates = convertedNHentaiToEHentaiFavorites
            .GroupBy(x => x.Value)
            .Where(x => x.Count() > 1);
        
        int duplicatesCount = duplicates
            .Select(x => x.Count())
            .Sum();
        
        if (duplicatesCount > 0)
        {
            Log.Information("{DuplicatesCount} entries were matched to duplicate targets", duplicatesCount);
            string duplicatesPath = Path.Combine(DataDirectory, "duplicatesNHentaiToEHentaiFavorites.txt");
            string duplicatesText = "";
            foreach (IGrouping<(string ID, string Token), KeyValuePair<string, (string, string)>> duplicateSet in duplicates)
            {
                duplicatesText += string.Join(", ", duplicateSet.Select(x => $"https://nhentai.org/g/{x.Key}/"));
                duplicatesText += $": https://exhentai.org/g/{duplicateSet.Key.ID}/{duplicateSet.Key.Token}/\n";
            }
            File.WriteAllText(duplicatesPath, duplicatesText);
            Log.Information("Written to file {DuplicatesPath}", duplicatesPath);
        }
    }


    public bool Authenticate(Direction direction)
    {
        Log.Information("Authenticating...");        

        using HttpResponseMessage response = GetRequest("https://e-hentai.org/favorites.php");
        string html = response.Content.ReadAsStringAsync().Result;
        if (html.Contains("Your IP address has been temporarily banned for excessive pageloads."))
        {
            throw new IPBannedException(Site.EHentai, response);
        }

        HtmlDocument htmlDoc = new();
        htmlDoc.LoadHtml(html);
        if (!htmlDoc.ParsedText.Contains("This page requires you to log on"))
        {
            if (direction == Direction.From)
            {
                BaseUrl = "https://e-hentai.org";
                Log.Information("Successfully authenticated.");
                return true;
            }
            else if (direction == Direction.To)
            {
                if (CanAccessExHentai())
                {
                    BaseUrl = "https://exhentai.org";
                    Log.Information("Successfully authenticated.");
                    return true;
                }
                else
                {
                    Log.Information("Failed to access ExHentai");
                    Log.Information("Proceed with migration using EHentai instead?");
                    Log.Information("Many conversions will fail [y/n]:");

                    if(Input.GetYesNoInput())
                    {
                        Log.Information("Proceeding with migration using EHentai...");
                        BaseUrl = "https://e-hentai.org";
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
        }
        else
        {
            throw new AuthenticationException($"Failed to authenticate");
        }
        return false;
    }


    public bool CanAccessExHentai()
    {
        Log.Information("Attempting to access ExHentai...");

        HttpResponseMessage response = GetRequest("https://exhentai.org");
        return !string.IsNullOrEmpty(response.Content.ReadAsStringAsync().Result);
    }


    private HttpResponseMessage GetRequest(string url)
    {
        double elapsedSinceLastRequest = RequestStopwatch.Elapsed.TotalSeconds;
        if (elapsedSinceLastRequest > RequestDelaySeconds)
        {
            RequestStopwatch.Restart();
            RequestCount++;
            HttpResponseMessage response = HttpClient.GetAsync(url).Result;
            Log.Debug("URL: {URL}, Status code: {StatusCode}, Total: {RequestCount}", url, (int)response.StatusCode, RequestCount);
            return response;
        }
        else
        {
            Thread.Sleep((int)((RequestDelaySeconds - elapsedSinceLastRequest) * 1000));
            return GetRequest(url);
        }
    }


    private HttpResponseMessage PostRequest(string url, HttpContent data)
    {
        double elapsedSinceLastRequest = RequestStopwatch.Elapsed.TotalSeconds;
        if (elapsedSinceLastRequest > RequestDelaySeconds)
        {
            RequestStopwatch.Restart();
            RequestCount++;
            HttpResponseMessage response = HttpClient.PostAsync(url, data).Result;
            Log.Debug("URL: {URL}, Data: {Data}, Status code: {StatusCode}, Total: {RequestCount}", url, data, (int)response.StatusCode, RequestCount);
            return response;
        }
        else
        {
            Thread.Sleep((int)((RequestDelaySeconds - elapsedSinceLastRequest) * 1000));
            return PostRequest(url, data);
        }
    }
}