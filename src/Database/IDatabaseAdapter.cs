using Newtonsoft.Json.Linq;

namespace DatabaseCompare.Database
{
    public interface IDatabaseAdapter
    {
        JArray GetRecords(string[] fields);
    }
}
