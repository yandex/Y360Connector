namespace Y360OutlookConnector.Configuration
{
    public class GeneralOptions
    {
        public bool UseDebugLevelLogging { get; set; } = true;
        public bool IsExternalBrowserUsedInLogin { get; set; }
        public bool HasMigratedToDebugLoggingByDefault { get; set; }
        public bool FormatFileAsLastNameFirst { get; set; } = false;
        public bool AutoLastFirstApplied { get; set; } = false;

        public GeneralOptions Clone() => (GeneralOptions)MemberwiseClone();
    }
}
