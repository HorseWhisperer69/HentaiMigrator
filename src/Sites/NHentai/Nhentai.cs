using System.Diagnostics;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using ShellProgressBar;

using HentaiMigrator.Enums;
using HentaiMigrator.Utils;
using HentaiMigrator.Interfaces;
using HentaiMigrator.Exceptions;
using Serilog;

namespace HentaiMigrator.Sites.NHentai;

public class NHentai: ISite
{
    public readonly double DefaultDelaySeconds = 1;
    public readonly int maxLeniency = 2;
    private readonly HttpClient HttpClient;
    private readonly Stopwatch RequestStopwatch;
    public double RequestDelaySeconds { get; private set; }
    public int RequestCount { get; private set; }
    private readonly Site WebSite = Site.NHentai;
    public const string BaseUrl = "https://nhentai.net";
    public readonly string DataDirectory;
    private readonly bool ContinueFromSavedData;

    public NHentai(string dataDirectory, WriteOnUpdateDictionary<string, string> authenticationCredentials, Direction direction, bool continueFromSavedData)
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
        HttpClient.DefaultRequestHeaders.Add("Referer", "https://nhentai.net");
        if (authenticationCredentials.ContainsKey("nhentaiCookie"))
        {
            HttpClient.DefaultRequestHeaders.Add("Cookie", authenticationCredentials["nhentaiCookie"]);
        } else
        {
            authenticationCredentials["nhentaiCookie"] = Input.GetCookieInput(WebSite);
            HttpClient.DefaultRequestHeaders.Add("Cookie", authenticationCredentials["nhentaiCookie"]);
        }
        
        bool authenticated;
        while (true)
        {
            try
            {
                authenticated = Authenticate();
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
                authenticationCredentials["nhentaiCookie"] = Input.GetCookieInput(WebSite);
                HttpClient.DefaultRequestHeaders.Add("User-Agent", authenticationCredentials["userAgent"]);
                HttpClient.DefaultRequestHeaders.Add("Cookie", authenticationCredentials["nhentaiCookie"]);
            }
        }
    }


    private List<string> GetFavoritesIDs()
    {
        Log.Information("Getting favorites IDs...", WebSite);

        List<string> favoritesIDs = new();

        HttpResponseMessage response = GetRequest("https://nhentai.net/favorites/");
        string html = response.Content.ReadAsStringAsync().Result;
        HtmlDocument htmlDoc = new();
        htmlDoc.LoadHtml(html);

        string favoritesText = htmlDoc.DocumentNode.SelectSingleNode("/html/body/div[2]/h1/span").InnerText.Replace(",", "").Replace("(", "").Replace(")", "");
        int numberOfFavorites = int.Parse(favoritesText);
        if (numberOfFavorites == 0)
        {
            return favoritesIDs;
        }

        int pageCount = numberOfFavorites / 25 + 1;
        int numberParsed = 0;
        using (ProgressBar pBar = new(numberOfFavorites, $"{numberParsed}/{numberOfFavorites}"))
        {
            int currPage = 1;
            while (true)
            {
                foreach (HtmlNode doujinshi in htmlDoc.DocumentNode.SelectNodes("/html/body/div[2]/div/div/div/a"))
                {
                    string doujinshiID = doujinshi.GetAttributeValue("href", "").Split("/")[2];
                    favoritesIDs.Add(doujinshiID);
                    numberParsed++;
                    pBar.Tick($"{numberParsed}/{numberOfFavorites}");
                }

                if (currPage == pageCount) break;
                else
                {
                    currPage++;
                    response = GetRequest($"https://nhentai.net/favorites/?page={currPage}");
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
        WriteOnUpdateDictionary<string, Dictionary<string, List<string>>> exportedFavorites = new(Path.Combine(DataDirectory, "exportedNHentaiFavorites.json"), initializeFromFile: ContinueFromSavedData);
        List<string> favoritesIDs = GetFavoritesIDs();

        if (favoritesIDs.Count == 0)
        {
            Log.Information("No favorites found");
            return new WriteOnUpdateDictionary<string, Dictionary<string, List<string>>>(Path.Combine(DataDirectory, "exportedNHentaiFavorites.json"), initializeFromFile: false);
        }

        Log.Information("Getting favorites metadata...");

        int numberOfFavorites = favoritesIDs.Count;
        int numberParsed = 0;
        using (ProgressBar pBar = new(numberOfFavorites, $"{numberParsed}/{numberOfFavorites}"))
        {
            foreach (string doujinshiID in favoritesIDs)
            {
                if (!exportedFavorites.ContainsKey(doujinshiID))
                {
                    exportedFavorites[doujinshiID] = GetDoujinshiInfo(doujinshiID);
                }
                numberParsed++;
                pBar.Tick($"{numberParsed}/{numberOfFavorites}");
            }
        }

        foreach (string nhentaiID in exportedFavorites.Select(x => x.Key))
        {
            if (!favoritesIDs.Contains(nhentaiID))
            {
                exportedFavorites.Remove(nhentaiID);
            }
        }

        return exportedFavorites;
    }


    private Dictionary<string, List<string>> GetDoujinshiInfo(string doujinshiID)
    {
        Dictionary<string, List<string>> doujinshiInfo = new();

        HttpResponseMessage response = GetRequest($"https://nhentai.net/api/gallery/{doujinshiID}");
        JsonElement json = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result).RootElement;
        
        doujinshiInfo["mediaID"] = new List<string>() {json.GetProperty("media_id").ToString()};
        doujinshiInfo["titleEN"] = new List<string>() {json.GetProperty("title").GetProperty("english").ToString()};
        doujinshiInfo["titleJP"] = new List<string>() {json.GetProperty("title").GetProperty("japanese").ToString()};
        doujinshiInfo["pages"] = new List<string>() {json.GetProperty("num_pages").ToString()};
        doujinshiInfo["uploadDate"] = new List<string>() {json.GetProperty("upload_date").ToString()};
        doujinshiInfo["language"] = new List<string>();
        doujinshiInfo["artist"] = new List<string>();
        doujinshiInfo["group"] = new List<string>();
        doujinshiInfo["parody"] = new List<string>();
        doujinshiInfo["category"] = new List<string>();
        doujinshiInfo["character"] = new List<string>();
        doujinshiInfo["tag"] = new List<string>();

        foreach (JsonElement tag in json.GetProperty("tags").EnumerateArray())
        {
            string type = tag.GetProperty("type").ToString();
            string name = tag.GetProperty("name").ToString();

            doujinshiInfo[type].Add(name);
        }
        
        return doujinshiInfo;
    }


    public void Import(WriteOnUpdateDictionary<string, Dictionary<string, List<string>>> exportedFavorites, Site siteFrom)
    {
        switch (siteFrom)
        {
            case Site.EHentai:
                WriteOnUpdateDictionary<string, string> convertedFavorites = null;

                for (int leniency = 0; leniency <= maxLeniency; leniency++)
                {
                    Log.Information("Converting EHentai favorites to NHentai at leniency level {Leniency}...", leniency);
                    convertedFavorites = ConvertEHentaiToNHentai(exportedFavorites, leniency);

                    if (convertedFavorites.Count == exportedFavorites.Count) break;
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


    private void AddToFavorites(WriteOnUpdateDictionary<string, string> convertedFavorites)
    {
        HttpResponseMessage response = GetRequest("https://nhentai.net");
        string html = response.Content.ReadAsStringAsync().Result;
        HtmlDocument htmlDoc = new();
        htmlDoc.LoadHtml(html);

        List<string> favoritesIDs = GetFavoritesIDs();

        Log.Information("Adding favorites...");
        
        int numberProcessed = 0;
        using (ProgressBar pBar = new(convertedFavorites.Count, $"{numberProcessed}/{convertedFavorites.Count}"))
        foreach (string favorite in convertedFavorites.Values)
        {
            if (favoritesIDs.Contains(favorite))
            {
                numberProcessed++;
                pBar.Tick($"{numberProcessed}/{convertedFavorites.Count}");
                continue;
            }

            double originalDelaySeconds = RequestDelaySeconds;
            int retryCount = 0;
            do
            {
                response = PostRequest($"https://nhentai.net/api/gallery/{favorite}/favorite");
                RequestDelaySeconds += 1;
                retryCount++;
                if (retryCount > 10)
                {
                    Log.Debug("Failed to add favorite {Favorite} after {RetryCount} retries", convertedFavorites.FirstOrDefault(x => x.Value == favorite), retryCount);
                    throw new IPBannedException(Site.NHentai, response);
                }
            } while (response.StatusCode != System.Net.HttpStatusCode.OK);

            numberProcessed++;
            pBar.Tick($"{numberProcessed}/{convertedFavorites.Count}");
            RequestDelaySeconds = originalDelaySeconds;
        }
    }


    public WriteOnUpdateDictionary<string, string> ConvertEHentaiToNHentai(WriteOnUpdateDictionary<string, Dictionary<string, List<string>>> parsedEHentaiFavorites, int leniency)
    {
        WriteOnUpdateDictionary<string, string> convertedEHentaiToNHentaiFavorites;
        WriteOnUpdateDictionary<string, Dictionary<string, string>> convertedEHentaiToNHentaiProgress;
        if (leniency == 0)
        {
            convertedEHentaiToNHentaiFavorites = new(Path.Combine(DataDirectory, "convertedEHentaiToNHentaiFavorites.json"), initializeFromFile: ContinueFromSavedData);
            convertedEHentaiToNHentaiProgress = new(Path.Combine(DataDirectory, "convertedEHentaiToNHentaiProgress.json"), initializeFromFile: ContinueFromSavedData);
        }
        else
        {
            convertedEHentaiToNHentaiFavorites = new(Path.Combine(DataDirectory, "convertedEHentaiToNHentaiFavorites.json"), initializeFromFile: true);
            convertedEHentaiToNHentaiProgress = new(Path.Combine(DataDirectory, "convertedEHentaiToNHentaiProgress.json"), initializeFromFile: true);
        }
        
        int numberProcessed = 0;
        using (ProgressBar pBar = new(parsedEHentaiFavorites.Count, $"{numberProcessed}/{parsedEHentaiFavorites.Count}"))
        foreach ((string ehentaiID, Dictionary<string, List<string>> ehentaiInfo) in parsedEHentaiFavorites)
        {
            if
            (
                convertedEHentaiToNHentaiProgress.ContainsKey(ehentaiID)
                &&
                    (convertedEHentaiToNHentaiProgress[ehentaiID]["converted"]  == "true"
                    ||
                    int.Parse(convertedEHentaiToNHentaiProgress[ehentaiID]["leniency"]) >= leniency)
            )
            {
                numberProcessed++;
                pBar.Tick($"{numberProcessed}/{parsedEHentaiFavorites.Count}");
                continue;
            }
            else
            {
                convertedEHentaiToNHentaiProgress[ehentaiID] = new()
                {
                    {"converted", "false"},
                    {"leniency", leniency.ToString()}
                };
            }

            string query = "https://nhentai.net/api/galleries/search?query=";
            switch (leniency)
            {
                case 0:
                    query += $"title:\"{ehentaiInfo["titleEN"][0]}\" ";
                    break;
                case 1:
                case 2:
                    query += $"pages:{ehentaiInfo["pages"][0]} ";
                    if (ehentaiInfo["artist"].Count > 0)
                    {
                        query += $"artist:\"{ehentaiInfo["artist"][0]}\" ";
                    }
                    if (ehentaiInfo["language"].Count > 0)
                    {
                        query += $"language:\"{ehentaiInfo["language"][0]}\" ";
                    }
                    break;
            }
            
            HttpResponseMessage response = GetRequest(query);
            if (response.Content.ReadAsStringAsync().Result.Contains("error")) // Not actually IP Banned, just a failed query
            {
                numberProcessed++;
                pBar.Tick($"{numberProcessed}/{parsedEHentaiFavorites.Count}");
                Log.Debug("Failed to convert EHentai ID {EHentaiID} with metadata {EHentaiInfo}", ehentaiID, ehentaiInfo);
                continue;
            }
            JsonElement json = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result).RootElement;

            if (json.GetProperty("num_pages").GetInt32() == 0)
            {
                numberProcessed++;
                pBar.Tick($"{numberProcessed}/{parsedEHentaiFavorites.Count}");
                Log.Debug("Failed to convert EHentai ID {EHentaiID} with metadata {EHentaiInfo}", ehentaiID, ehentaiInfo);
                continue;
            }

            string? doujinshiID = null;
            JsonElement[] matchedFavorites = json.GetProperty("result").EnumerateArray().ToArray();
            foreach (JsonElement matchedFavorite in matchedFavorites)
            {
                switch (leniency)
                {
                    case 0:
                    case 1:
                        if (matchedFavorite.GetProperty("media_id").ToString() == ehentaiID)
                        {
                            doujinshiID = matchedFavorite.GetProperty("id").ToString();
                        }
                        break;
                    case 2:
                        if (matchedFavorite.GetProperty("media_id").ToString() == ehentaiID ||
                            matchedFavorite.GetProperty("title").GetProperty("english").ToString() == ehentaiInfo["titleEN"][0])
                        {
                            doujinshiID = matchedFavorite.GetProperty("id").ToString();
                        }
                        break;
                }
                if (doujinshiID != null) break;
            }

            if (doujinshiID == null)
            {
                numberProcessed++;
                pBar.Tick($"{numberProcessed}/{parsedEHentaiFavorites.Count}");
                Log.Debug("Failed to convert EHentai ID {EHentaiID} with metadata {EHentaiInfo}", ehentaiID, ehentaiInfo);
                continue;
            }

            convertedEHentaiToNHentaiProgress[ehentaiID]["converted"] = "true";
            convertedEHentaiToNHentaiProgress.WriteToFile();

            convertedEHentaiToNHentaiFavorites[ehentaiID] = doujinshiID;
            numberProcessed++;
            pBar.Tick($"{numberProcessed}/{parsedEHentaiFavorites.Count}");
        }
        
        ConvertEHentaiToNHentaiProcessFailed(parsedEHentaiFavorites, convertedEHentaiToNHentaiProgress);
        ConvertEHentaiToNHentaiProcessDuplicates(parsedEHentaiFavorites, convertedEHentaiToNHentaiFavorites);

        return convertedEHentaiToNHentaiFavorites;
    }


    private void ConvertEHentaiToNHentaiProcessFailed(WriteOnUpdateDictionary<string, Dictionary<string, List<string>>> parsedEHentaiFavorites, WriteOnUpdateDictionary<string, Dictionary<string, string>> convertedEHentaiToNHentaiProgress)
    {
        if (convertedEHentaiToNHentaiProgress.Values.Any(x => x["converted"] == "false"))
        {
            Log.Information("Failed to convert {FailedToConvertCount} entries", convertedEHentaiToNHentaiProgress.Values.Count(x => x["converted"] == "false"));
            string failedToConvertPath = Path.Combine(DataDirectory, "failedToConvertEHentaiToNHentaiFavorites.txt");
            string failedToConvertText = "";
            foreach ((string iehentaiID, Dictionary<string, string> iProgress) in convertedEHentaiToNHentaiProgress)
            {
                if (iProgress["converted"] == "false")
                {
                    failedToConvertText += $"https://exhentai.org/g/{iehentaiID}/{parsedEHentaiFavorites[iehentaiID]["doujinshiToken"][0]}/: {parsedEHentaiFavorites[iehentaiID]["titleEN"][0]}\n";
                }
            }
            File.WriteAllText(failedToConvertPath, failedToConvertText);
            Log.Information("Written to file {FailedToConvertPath}", failedToConvertPath);
        }
    }


    private void ConvertEHentaiToNHentaiProcessDuplicates(WriteOnUpdateDictionary<string, Dictionary<string, List<string>>> parsedEHentaiFavorites, WriteOnUpdateDictionary<string, string> convertedEHentaiToNHentaiFavorites)
    {
        IEnumerable<IGrouping<string, KeyValuePair<string, string>>> duplicates = convertedEHentaiToNHentaiFavorites
            .GroupBy(x => x.Value)
            .Where(x => x.Count() > 1);
        
        int duplicatesCount = duplicates
            .Select(x => x.Count())
            .Sum();
        
        if (duplicatesCount > 0)
        {
            Log.Information("{DuplicatesCount} entries were matched to duplicate targets", duplicatesCount);
            string duplicatesPath = Path.Combine(DataDirectory, "duplicatesEHentaiToNHentaiFavorites.txt");
            string duplicatesText = "";
            foreach (IGrouping<string, KeyValuePair<string, string>> duplicateSet in duplicates)
            {
                duplicatesText += string.Join(", ", duplicateSet.Select(x => $"https://exhentai.org/g/{x.Key}/{parsedEHentaiFavorites[x.Key]["doujinshiToken"][0]}/"));
                duplicatesText += $": https://nhentai.org/g/{duplicateSet.Key}/\n";
            }
            File.WriteAllText(duplicatesPath, duplicatesText);
            Log.Information("Written to file {DuplicatesPath}", duplicatesPath);
        }
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


    private HttpResponseMessage PostRequest(string url, HttpContent? data = null)
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

    
    public bool Authenticate()
    {
        Log.Information("Authenticating...");

        using HttpResponseMessage response = GetRequest("https://nhentai.net/favorites/");
        string html = response.Content.ReadAsStringAsync().Result;

        HtmlDocument htmlDoc = new();
        htmlDoc.LoadHtml(html);

        if (htmlDoc.GetElementbyId("favcontainer") != null)
        {
            Regex regex = new(@"csrf_token: ""(\w+)""");
            Match match = regex.Match(htmlDoc.DocumentNode.SelectSingleNode("/html/body/script[1]").InnerText);
            string csrfToken = match.Groups[1].Value;
            HttpClient.DefaultRequestHeaders.Add("X-CSRFToken", csrfToken);
            
            Log.Information("Successfully authenticated");
            return true;
        }
        else
        {
            throw new AuthenticationException("Failed to authenticate");
        }
    }
}
