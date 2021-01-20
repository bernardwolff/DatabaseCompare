namespace DatabaseCompare.Config
{
    public enum DatabaseType
    {
        MongoDB,
        SQLServer
    }

    public class DatabaseSettings
    {
        public DatabaseType DatabaseType { get; set; }
        public string ConnString { get; set; }
        public string TableName { get; set; }
        public string Query { get; set; }
        public string Aggregate { get; set; }
    }
}
