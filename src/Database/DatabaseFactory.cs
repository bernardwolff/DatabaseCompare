using System;
using DatabaseCompare.Config;

namespace DatabaseCompare.Database
{
    public class DatabaseFactory
    {
        public static IDatabaseAdapter GetDatabaseAdaptor(DatabaseSettings settings)
        {
            switch (settings.DatabaseType)
            {
                case DatabaseType.MongoDB:
                    return new MongoDatabaseAdapter(settings);
                case DatabaseType.SQLServer:
                    return new SQLServerDatabaseAdapter(settings);
                default:
                    throw new NotSupportedException(settings.DatabaseType + " is not supported.");
            }
        }
    }
}
