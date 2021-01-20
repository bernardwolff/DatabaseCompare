using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DatabaseCompare.Config;
using DatabaseCompare.Database;
using JsonDiffPatchDotNet;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Logger = NLog.Logger;

namespace DatabaseCompare
{
    public class Comparer
    {
        private readonly Comparison _comparison;
        private readonly string _outputFolder;
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public Comparer(Comparison comparison, string outputFolder)
        {
            _comparison = comparison;
            _outputFolder = outputFolder;
        }

        public static IEnumerable<Comparer> Construct(string configFile, string outputFolder)
        {
            using (var file = File.OpenText(configFile))
            {
                var serializer = new JsonSerializer();
                var config = (Config.Config)serializer.Deserialize(file, typeof(Config.Config));
                return config.Comparisons.Select(c => new Comparer(c, outputFolder));
            }
        }

        public void Compare()
        {
            _logger.Debug("Running comparison: " + _comparison.ComparisonName);

            var fieldsToSelect = _comparison.MatchFields.Union(_comparison.CompareFields).ToArray();


            var sourceSettings = _comparison.SourceSettings;
            var source = DatabaseFactory.GetDatabaseAdaptor(sourceSettings);
            var sourceRecords = source.GetRecords(fieldsToSelect).ToList();

            _logger.Trace("Got source records");

            var targetSettings = _comparison.TargetSettings;
            var target = DatabaseFactory.GetDatabaseAdaptor(targetSettings);
            var targetRecords = target.GetRecords(fieldsToSelect).ToList();

            _logger.Trace("Got target records");

            var jdp = new JsonDiffPatch();
            var equalityComparer = new RecordEqualityComparer(_comparison.MatchFields);
            var sourceOnly = new JArray(sourceRecords.Except(targetRecords, equalityComparer).ToList());
            var targetOnly = new JArray(targetRecords.Except(sourceRecords, equalityComparer).ToList());
            var intersection = new JArray(sourceRecords.Intersect(targetRecords, equalityComparer).ToList());

            _logger.Trace("Got intersection");

            var differingDocs = 0;
            var identicalDocs = 0;
            var i = 0;

            var recordComparisonStartTime = DateTime.Now;
            var patches = new ConcurrentBag<JToken>();

            var sourceRecordsDict = sourceRecords.ToDictionary(k => equalityComparer.GetKey(k), v => (JObject)v);
            var targetRecordsDict = targetRecords.ToDictionary(k => equalityComparer.GetKey(k), v => (JObject)v);

            _logger.Trace("Converted to dictionary");

            var differencesMatchFields = new ConcurrentBag<JToken>();

            Parallel.ForEach(intersection, record =>
            //foreach (var record in intersection)
            {
                var sourceRecord = sourceRecordsDict[equalityComparer.GetKey(record)];
                var targetRecord = targetRecordsDict[equalityComparer.GetKey(record)];

                // TODO: can we configure jdp to treat null and empty string as equal?
                var patch = jdp.Diff(sourceRecord, targetRecord);
                var newi = Interlocked.Increment(ref i);

                if (patch == null)
                {
                    Interlocked.Increment(ref identicalDocs);
                    return;
                }

                Interlocked.Increment(ref differingDocs);

                foreach (var matchField in _comparison.MatchFields)
                {
                    patch[matchField] = record[matchField];
                }

                patches.Add(patch);

                var matchFields = string.Join(",", _comparison.MatchFields.Where(mf => !string.IsNullOrEmpty(mf)).Select(mf => record[mf]).Where(m => !string.IsNullOrEmpty(m?.ToString())));

                if (!string.IsNullOrEmpty(matchFields))
                {
                    var str = JValue.CreateString(matchFields);
                    differencesMatchFields.Add(str);
                }

                PrintStatus(recordComparisonStartTime, newi, intersection.Count);
            });

            PrintStatus(recordComparisonStartTime, i, intersection.Count);
            Console.WriteLine();
            
            WriteFile(new JArray(patches), Path.Combine(_outputFolder, _comparison.DifferencesFilename));
            WriteFile(sourceOnly, Path.Combine(_outputFolder, _comparison.SourceOnlyFilename));
            WriteFile(targetOnly, Path.Combine(_outputFolder, _comparison.TargetOnlyFilename));
            WriteFile(new JArray(differencesMatchFields), Path.Combine(_outputFolder, _comparison.DifferencesMatchFieldsFilename));

            using (var summaryFile = File.Create(Path.Combine(_outputFolder, _comparison.SummaryExportFilename)))
            using (var streamWriter = new StreamWriter(summaryFile))
            {
                streamWriter.WriteLine(_comparison.ComparisonName);
                streamWriter.WriteLine(new string('=', _comparison.ComparisonName.Length));
                Tee(streamWriter, sourceRecords.Count() + " total source records");
                Tee(streamWriter, targetRecords.Count() + " total target records");
                Tee(streamWriter, identicalDocs + " identical documents");
                Tee(streamWriter, differingDocs + " differing documents");
                Tee(streamWriter, sourceOnly.Count + " documents with no target match");
                Tee(streamWriter, targetOnly.Count + " documents with no source match");
                Tee(streamWriter, intersection.Count + " documents in both source and target");
                Tee(streamWriter, "Match fields: " + string.Join(", ", _comparison.MatchFields));
                Tee(streamWriter, "Compare fields: " + string.Join(", ", _comparison.CompareFields));
            }
        }

        

        private void PrintStatus(DateTime startTime, int currentIndex, int totalCount)
        {
            var totalTime = DateTime.Now - startTime;

            var averageMilliseconds = Math.Round(totalTime.TotalMilliseconds / currentIndex, 5).ToString("0.00000").PadLeft(9);
            Console.Write(
                $"\rcompared {currentIndex.ToString().PadLeft(7)} of {totalCount} records. {averageMilliseconds} milliseconds average per record");
        }

        private void WriteFile(JArray jarray, string filename)
        {
            if (string.IsNullOrEmpty(filename) || jarray == null || !jarray.Any()) return;

            using (var filestream = File.Create(filename))
            using (var streamWriter = new StreamWriter(filestream))
            {
                var jarrayStr = new JArray(jarray.Where(j => j != null)).ToString();
                streamWriter.WriteLine(jarrayStr);
            }
        }

        /// <summary>
        /// Write to log and to a stream. Inspired by the Unix command of the same name.
        /// </summary>
        private void Tee(StreamWriter streamWriter, string msg)
        {
            streamWriter.WriteLine(msg);
            _logger.Debug(msg);
        }

        private class RecordEqualityComparer : IEqualityComparer<JToken>
        {
            private readonly string[] _matchFields;

            public RecordEqualityComparer(string[] matchFields)
            {
                _matchFields = matchFields;
            }

            public bool Equals(JToken x, JToken y)
            {
                if (x == null && y == null)
                    return true;

                if (x == null)
                    return false;

                if (y == null)
                    return false;

                return _matchFields.All(mf => JToken.DeepEquals(x[mf], y[mf]));
            }

            public int GetHashCode(JToken obj)
            {
                return GetKey(obj).GetHashCode();
            }

            public string GetKey(JToken obj)
            {
                var combinedKey = "";
                foreach (var matchField in _matchFields)
                {
                    combinedKey += obj[matchField];
                }
                return combinedKey;
            }
        }
    }
}
