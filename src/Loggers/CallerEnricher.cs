using System.Diagnostics;
using System.Reflection;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace HentaiMigrator.Logger;

class CallerEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        int skip = 3;
        while (true)
        {
            StackFrame stack = new(skip);
            if (!stack.HasMethod())
            {
                logEvent.AddPropertyIfAbsent(new LogEventProperty("Caller", new ScalarValue("<unknown method>")));
                return;
            }

            MethodBase method = stack.GetMethod();
            if (method.DeclaringType.Assembly != typeof(Log).Assembly)
            {
                string callerNamespace = method.DeclaringType.Namespace;
                string callerClass = method.DeclaringType.Name;
                string callerMethod = method.Name;
                logEvent.AddPropertyIfAbsent(new LogEventProperty("Namespace", new ScalarValue(callerNamespace)));
                logEvent.AddPropertyIfAbsent(new LogEventProperty("Class", new ScalarValue(callerClass)));
                logEvent.AddPropertyIfAbsent(new LogEventProperty("Method", new ScalarValue(callerMethod)));
                return;
            }

            skip++;
        }
    }
}