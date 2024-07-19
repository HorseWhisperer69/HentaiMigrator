using Serilog;
using Serilog.Configuration;

namespace HentaiMigrator.Logger;

static class LoggerCallerEnrichmentConfiguration
{
    public static LoggerConfiguration WithCaller(this LoggerEnrichmentConfiguration enrichmentConfiguration)
    {
        return enrichmentConfiguration.With<CallerEnricher>();
    }
}