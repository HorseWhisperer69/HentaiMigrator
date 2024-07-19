using HentaiMigrator.Utils;
using HentaiMigrator.Enums;

namespace HentaiMigrator.Interfaces;

public interface ISite
{
    public WriteOnUpdateDictionary<string, Dictionary<string, List<string>>> Export();
    public void Import(WriteOnUpdateDictionary<string, Dictionary<string, List<string>>> exportedFavorites, Site siteFrom);
}
