namespace Y360OutlookConnector.Configuration
{
    public class GeneralOptions
    {
        public bool UseDebugLevelLogging { get; set; }
        public bool IsExternalBrowserUsedInLogin { get; set; }

        public GeneralOptions Clone() => (GeneralOptions)MemberwiseClone();
    }
}
