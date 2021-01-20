using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DatabaseCompare.Config;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Newtonsoft.Json.Linq;

namespace DatabaseCompare.Database
{
    public class MongoDatabaseAdapter : IDatabaseAdapter
    {
        private readonly IMongoDatabase _sourceDb;
        private readonly string _tableName;
        private readonly string _query;
        private readonly string _aggregate;
        private readonly DateTime EPOC = new DateTime(1970, 1, 1, 0, 0, 0);

        public MongoDatabaseAdapter(DatabaseSettings settings)
        {
            _sourceDb = new MongoClient(settings.ConnString)
                .GetDatabase(MongoUrl.Create(settings.ConnString).DatabaseName);
            _tableName = settings.TableName;
            _query = settings.Query;
            _aggregate = settings.Aggregate;
        }

        public JArray GetRecords(string[] fields)
        {
            var collection = _sourceDb.GetCollection<BsonDocument>(_tableName);
            var projection = Builders<BsonDocument>.Projection.Exclude("_id");
            foreach (var field in fields)
            {
                projection = projection.Include(field);
            }


            List<BsonDocument> documents;
            var query = new BsonDocument();

            if (!string.IsNullOrEmpty(_aggregate))
            {
                var pipeArray = JArray.Parse(_aggregate);
                var pipeline = pipeArray.Select(p => BsonDocument.Parse(p.ToString())).ToList();
                documents = collection.Aggregate<BsonDocument>(pipeline).ToList();
            }
            else
            {
                if (!string.IsNullOrEmpty(_query))
                {
                    query = BsonSerializer.Deserialize<BsonDocument>(_query);
                }
                documents = collection.Find(query).Project(projection).ToList();
            }
            
            var jsonWriterSettings = new JsonWriterSettings { OutputMode = JsonOutputMode.Strict };
            var json = documents.ToJson(jsonWriterSettings);
            var records = JArray.Parse(json);
            
            foreach (var record in records)
            {
                SetUnsetFieldsToNull((JObject)record, fields);
                FixDateFields((JObject)record, fields);
            }

            return records;
        }

        private void SetUnsetFieldsToNull(JObject obj, string[] fields)
        {
            foreach (var field in fields)
            {
                // unset fields
                if (!obj.TryGetValue(field, out var _))
                {
                    obj[field] = null;
                }
                
                // empty strings
                if (obj[field] != null && obj[field].Type == JTokenType.String && obj[field].ToString() == "")
                {
                    // convert empty strings to null
                    obj[field] = null;
                }
            }
        }

        private void FixDateFields(JObject obj, string[] fields)
        {
            foreach (var field in fields)
            {
                if (obj.TryGetValue(field, out var dateObj) && dateObj.Type == JTokenType.Object && ((JObject)dateObj).TryGetValue("$date", out var dateVal) && dateVal.Type == JTokenType.Integer)
                {
                    obj[field] = EPOC.AddMilliseconds((long)dateVal).Date;
                }
            }
        }
    }
}
