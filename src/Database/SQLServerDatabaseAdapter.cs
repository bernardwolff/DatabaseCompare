using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Data.SqlClient;
using System.Text.RegularExpressions;
using Dapper;
using DatabaseCompare.Config;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DatabaseCompare.Database
{
    public class SQLServerDatabaseAdapter : IDatabaseAdapter
    {
        private readonly SqlConnection _connection;
        private readonly DatabaseSettings _settings;

        public SQLServerDatabaseAdapter(DatabaseSettings settings)
        {
            _connection = new SqlConnection(settings.ConnString);
            _connection.Open();
            _settings = settings;
        }

        public JArray GetRecords(string[] fields)
        {
            var query = GetQuery(fields);

            var records = new List<JObject>();
            using (var reader = _connection.ExecuteReader(query.Sql))
            {
                while (reader.Read())
                {
                    records.Add(GetRecord(reader, query));
                }
            }

            var json = JsonConvert.SerializeObject(records);
            return JArray.Parse(json);
        }

        private JObject GetRecord(IDataReader reader, Query query)
        {
            var d = new JObject();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var fieldName = reader.GetName(i);
                
                if (!query.FieldsToSelect.Contains(fieldName))
                {
                    continue;
                }

                var fieldValue = new JValue(reader.GetValue(i));

                if (!query.ArrayFieldNamesMapping.ContainsKey(fieldName))
                {
                    var dataType = reader.GetDataTypeName(i);
                    if (dataType.ToLower().Contains("varchar") && fieldValue.ToString() == "")
                    {
                        // convert empty strings to null
                        fieldValue = null;
                    }
                    d[fieldName] = fieldValue;
                    continue;
                }

                CreateArray(query, fieldName, d, fieldValue);
            }

            return d;
        }

        private void CreateArray(Query query, string fieldName, JObject d, JValue fieldValue)
        {
            var mappedFieldName = query.ArrayFieldNamesMapping[fieldName];
            if (!d.TryGetValue(mappedFieldName, out JToken _))
            {
                d[mappedFieldName] = null;
            }

            if (fieldValue.Type == JTokenType.Null)
            {
                return;
            }

            if (d[mappedFieldName] == null || d[mappedFieldName].Type == JTokenType.Null)
            {
                d[mappedFieldName] = new JArray();
            }
            ((JArray)d[mappedFieldName]).Add(fieldValue);
        }

        private Query GetQuery(string[] fields)
        {
            var query = new Query();
            var schemaQuery = string.IsNullOrEmpty(_settings.Query) ? $"select * from {_settings.TableName}" : _settings.Query;
            var dr = _connection.ExecuteReader(schemaQuery);
            var schemaFields =
                dr.GetSchemaTable()?.Rows.Cast<DataRow>().Select(c => c["ColumnName"].ToString()).ToList() ??
                new List<string>();

            foreach (var schemaField in schemaFields)
            {
                var match = Regex.Match(schemaField, @"(\w+)\.[0-9]+");
                if (match.Success)
                {
                    var baseFieldName = match.Groups[1].Value;
                    if (fields.Contains(baseFieldName))
                    {
                        query.FieldsToSelect.Add(schemaField);
                        query.ArrayFieldNamesMapping.Add(schemaField, baseFieldName);
                    }
                    continue;
                }

                if (fields.Contains(schemaField))
                {
                    query.FieldsToSelect.Add(schemaField);
                }
            }

            query.Sql = string.IsNullOrEmpty(_settings.Query)
                ? $"select [{string.Join("],[", query.FieldsToSelect)}] from {_settings.TableName}"
                : _settings.Query;

            return query;
        }

        class Query
        {
            public List<string> FieldsToSelect { get; set; }
            public string Sql { get; set; }
            public Dictionary<string, string> ArrayFieldNamesMapping { get; set; }

            public Query()
            {
                FieldsToSelect = new List<string>();
                ArrayFieldNamesMapping = new Dictionary<string, string>();
            }
        }
    }
}
