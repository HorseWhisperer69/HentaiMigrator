using HentaiMigrator.Enums;

namespace HentaiMigrator.Exceptions;

public class IPBannedException: Exception
{
    new public readonly string Message;
    public readonly HttpResponseMessage Response;
    public readonly Site Site;
    public IPBannedException(Site site, HttpResponseMessage response)
    {
        Message = "";
        Response = response;
        Site = site;
    }
    public IPBannedException(Site site, string message, HttpResponseMessage response)
    {
        Message = message;
        Response = response;
        Site = site;
    }
}
