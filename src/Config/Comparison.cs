namespace DatabaseCompare.Config
{
    public class Comparison
    {
        public string ComparisonName { get; set; }
        public string SummaryExportFilename { get; set; }
        public string DifferencesFilename { get; set; }
        public string SourceOnlyFilename { get; set; }
        public string TargetOnlyFilename { get; set; }
        public string DifferencesMatchFieldsFilename { get; set; }
        public DatabaseSettings SourceSettings { get; set; }
        public DatabaseSettings TargetSettings { get; set; }
        public string[] MatchFields { get; set; }
        public string[] CompareFields { get; set; }
    }
}
