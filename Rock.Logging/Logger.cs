using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Rock.Logging
{
    public class Logger : ILogger
    {
        private readonly ILoggerConfiguration _configuration;
        private readonly IEnumerable<ILogProvider> _logProviders;

        private readonly string _applicationId;
        
        private readonly ILogProvider _auditLogProvider;
        private readonly IThrottlingRuleEvaluator _throttlingRuleEvaluator;
        private readonly IEnumerable<IContextProvider> _contextProviders;

        public Logger(
            ILoggerConfiguration configuration,
            IEnumerable<ILogProvider> logProviders,
            IApplicationIdProvider applicationIdProvider = null,
            ILogProvider auditLogProvider = null,
            IThrottlingRuleEvaluator throttlingRuleEvaluator = null,
            IEnumerable<IContextProvider> contextProviders = null)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException("configuration");
            }

            if (logProviders == null)
            {
                throw new ArgumentNullException("logProviders");
            }

            // Be sure to fully realize lists so we get fast enumeration during logging.
            logProviders = logProviders.ToList();

            if (!logProviders.Any())
            {
                throw new ArgumentException("Must provide at least one log provider.", "logProviders");
            }

            _configuration = configuration;
            _logProviders = logProviders;

            _applicationId =
                applicationIdProvider != null
                    ? applicationIdProvider.GetApplicationId()
                    : ApplicationId.Current;

            _auditLogProvider = auditLogProvider; // NOTE: this can be null, and is expected.
            _throttlingRuleEvaluator = throttlingRuleEvaluator ?? new NullThrottlingRuleEvaluator();
            _contextProviders = (contextProviders ?? Enumerable.Empty<IContextProvider>()).ToList();
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return
                _configuration.IsLoggingEnabled
                && logLevel >= _configuration.LoggingLevel
                && logLevel != LogLevel.NotSet;
        }

        public async Task LogAsync(
            ILogEntry logEntry,
            [CallerMemberName] string callerMemberName = null,
            [CallerFilePath] string callerFilePath = null,
            [CallerLineNumber] int callerLineNumber = 0)
        {
            if (logEntry.Level != LogLevel.Audit
                && (!IsEnabled(logEntry.Level)
                    || (_throttlingRuleEvaluator != null && !_throttlingRuleEvaluator.ShouldLog(logEntry))))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(logEntry.ApplicationId))
            {
                logEntry.ApplicationId = _applicationId;
            }

            if (logEntry.UniqueId == null)
            {
                logEntry.UniqueId = Guid.NewGuid().ToString();
            }

            // ReSharper disable ExplicitCallerInfoArgument
            logEntry.AddCallerInfo(callerMemberName, callerFilePath, callerLineNumber);
            // ReSharper restore ExplicitCallerInfoArgument

            foreach (var contextProvider in _contextProviders)
            {
                contextProvider.AddContextData(logEntry);
            }

            OnPreLog(logEntry);

            Task writeTask;

            if (logEntry.Level == LogLevel.Audit && _auditLogProvider != null)
            {
                writeTask = _auditLogProvider.WriteAsync(logEntry);
            }
            else
            {
                writeTask =
                    Task.WhenAll(
                        _logProviders
                            .Where(x => logEntry.Level >= x.LoggingLevel)
                            .Select(logProvider => logProvider.WriteAsync(logEntry)));
            }

            try
            {
                await writeTask;
            }
            catch (Exception ex)
            {
                // TODO: Send log entry and exception(s) to system event log. AND/OR, sent it to a retry mechanism.
                // TODO ALSO: The error handling here sucks. Do something about it.
            }
        }

        protected virtual void OnPreLog(ILogEntry logEntry)
        {
        }
    }
}