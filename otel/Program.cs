namespace otel
{
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using OpenTelemetry;
    using OpenTelemetry.Logs;
    using OpenTelemetry.Resources;
    using OpenTelemetry.Exporter.Geneva;
    using OpenTelemetry.Metrics;
    using System.Diagnostics.Metrics;
    using System;

    class Program
    {
        // Environment variable key for retrieving the container name
        const string container_name_env_key = "Fabric_CodePackageName";

        // OpenTelemetry meter and counter for CPU usage metrics
        private static readonly Meter CPUMeter = new("Com.Microsoft.ACI.Samples", "1.0");
        private static readonly Counter<long> CpuUsageCounter = CPUMeter.CreateCounter<long>("CPU");

        static void Main(string[] args)
        {
            // Get container group name from environment variable
            var cgname = Environment.GetEnvironmentVariable(container_name_env_key);

            // File paths for MDM and MDSD sockets
            string mdm_filePath = "/var/etw/mdm_ifx.socket";
            string mdsd_filePath = "/var/run/mdsd/default_fluent.socket";

            // Retry configuration for checking file existence
            int maxAttempts = 10;      // Maximum number of attempts
            int attemptDelay = 5000;   // Delay between attempts (in milliseconds)

            Console.WriteLine($"Checking for files: '{mdm_filePath}' and '{mdsd_filePath}'.");

            // Retry loop to check for file existence
            for (int i = 0; i < maxAttempts; i++)
            {
                if (File.Exists(mdm_filePath) && File.Exists(mdsd_filePath))
                {
                    Console.WriteLine($"Files '{mdm_filePath}' and '{mdsd_filePath}' found after {i + 1} attempts.");
                    break; // Exit the loop once files are found
                }

                Console.WriteLine($"Files not found. Attempt {i + 1} of {maxAttempts}.");
                Thread.Sleep(attemptDelay); // Wait before retrying
            }

            // Final check if files exist
            if (!File.Exists(mdm_filePath) || !File.Exists(mdsd_filePath))
            {
                Console.WriteLine($"Files '{mdm_filePath}' or '{mdsd_filePath}' not found after {maxAttempts} attempts.");
                return; // Exit the program if files are missing
            }

            Console.WriteLine($"Files '{mdm_filePath}' and '{mdsd_filePath}' found. Proceeding with execution...");

            // Configure OpenTelemetry logging with Geneva exporter
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddOpenTelemetry(options =>
                {
                    options.AddGenevaLogExporter(genevaExporterOptions =>
                    {
                        genevaExporterOptions.ConnectionString = "Endpoint=unix:/var/run/mdsd/default_fluent.socket";
                        genevaExporterOptions.PrepopulatedFields = new Dictionary<string, object>()
                        {
                            ["Environment"] = "PROD",
                            ["Region"] = "EastUS"
                        };
                    });
                });
            });

            // Create a logger instance
            var logger = loggerFactory.CreateLogger<Program>();

            // Logging thread
            var logThread = new Thread(() =>
            {
                while (true)
                {
                    Console.WriteLine($"{cgname} : Logging messages at {DateTime.UtcNow}");
                    logger.LogInformation($"{cgname} : Logging information message at {DateTime.UtcNow}");
                    logger.LogError($"{cgname} : Logging error message at {DateTime.UtcNow}");
                    Thread.Sleep(10000); // Log every 10 seconds
                }
            });

            // Configure OpenTelemetry metrics with Geneva exporter
            using var meterProvider = Sdk.CreateMeterProviderBuilder()
                .AddMeter(CPUMeter.Name)
                .AddGenevaMetricExporter(genevaExporterOptions =>
                {
                    genevaExporterOptions.ConnectionString = $"Endpoint=unix:/var/etw/mdm_ifx.socket;Account= MicrosoftContainerInstanceShoeboxDev;Namespace=AzureMonitoringMetrics";
                    genevaExporterOptions.PrepopulatedMetricDimensions = new Dictionary<string, object>()
                    {
                        ["environment"] = "PROD",
                        ["cloud.region"] = "EastUS"
                    };
                })
                .Build();

            // Metric emission thread
            var metricThread = new Thread(() =>
            {
                Random random = new Random();
                while (true)
                {
                    // Generate a random CPU usage value (1-100)
                    long cpuUsage = random.Next(1, 101);
                    Console.WriteLine($"{cgname} : Emitting metric {cpuUsage} millicores");

                    // Emit the CPU usage metric with associated attributes
                    CpuUsageCounter.Add(cpuUsage,
                        new("resource.name", $"{cgname}"),
                        new("resource.location", "eastus"));

                    Thread.Sleep(5000); // Emit every 5 seconds
                }
            });

            // Start both threads
            logThread.Start();
            metricThread.Start();

            // Keep the main thread alive
            logThread.Join();
            metricThread.Join();
        }
    }
}
