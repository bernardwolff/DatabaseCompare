using System;
using System.IO;
using NLog;
using Logger = NLog.Logger;

namespace DatabaseCompare
{
    class Program
    {
        private static Logger _logger = LogManager.GetCurrentClassLogger();

        static void Main(string[] args)
        {
            try
            {
                MainInternal(args);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Exception Message: {ex.Message}");
            }
            finally
            {
                LogManager.Shutdown();
                Console.WriteLine("Done.");
            }
        }

        static void MainInternal(string[] args)
        {
            if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
            {
                _logger.Error("No config file provided.");
                return;
            }

            var configFile = args[0];
            
            if (!File.Exists(configFile))
            {
                _logger.Error($"Cannot find config file at: ${configFile}");
            }

            var outputLocation = Path.Combine("output", DateTime.Now.ToString("yyyy-MM-dd hhmm tt"));
            Directory.CreateDirectory(outputLocation);

            var comparers = Comparer.Construct(configFile, outputLocation);
            foreach (var comparer in comparers)
            {
                comparer.Compare();
                GC.Collect();
            }
        }
    }
}
