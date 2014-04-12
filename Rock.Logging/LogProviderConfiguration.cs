using System;

namespace Rock.Logging
{
    public class LogProviderConfiguration : ILogProviderConfiguration
    {
        public string FormatterName { get; set; }
        public Type ProviderType { get; set; }
    }
}