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
        const string container_name_env_key = "Fabric_CodePackageName";

        private static readonly Meter CPUMeter = new("Com.Microsoft.ACI.Samples", "1.0");
        private static readonly Counter<long> CpuUsageCounter = CPUMeter.CreateCounter<long>("CPU");

        static void Main(string[] args)
        {
            var cgname = Environment.GetEnvironmentVariable(container_name_env_key);

            string mdm_filePath = "/var/etw/mdm_ifx.socket";
            string mdsd_filePath = "/var/run/mdsd/default_fluent.socket";
            int maxAttempts = 10; // Maximum number of attempts to check
            int attemptDelay = 5000; // Delay between attempts in milliseconds

            Console.WriteLine($"Checking for file '{mdm_filePath}'..and {mdsd_filePath}.");

            for (int i = 0; i < maxAttempts; i++)
            {
                if (File.Exists(mdm_filePath) && File.Exists(mdsd_filePath))
                {
                    Console.WriteLine($"File '{mdm_filePath}' and '{mdsd_filePath}' found after {i + 1} attempts.");
                    // Proceed with your operations here
                    break; // Exit the loop once the file is found
                }

                Console.WriteLine($"File '{mdm_filePath}' and '{mdsd_filePath}' not found. Attempt {i + 1} of {maxAttempts}.");
                Thread.Sleep(attemptDelay); // Wait before the next attempt
            }

            if (File.Exists(mdm_filePath) && File.Exists(mdsd_filePath))
            {
                Console.WriteLine($"File '{mdm_filePath}' and '{mdsd_filePath}' found ..");
                // Handle the case where the file was not found within the allowed attempts
            }
            else 
            {
                Console.WriteLine($"File '{mdm_filePath}' and '{mdsd_filePath}' not found after {maxAttempts} attempts.");
                return;
            }

            // Configure a logger factory
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                // Add OpenTelemetry logging
                builder.AddOpenTelemetry(options =>
                {
                    // Configure a log exporter (Console in this example)
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

            var logThread = new Thread(() =>
            {
                while (true)
                {
                    Console.WriteLine($"{cgname} : Logging message at {DateTime.UtcNow}");
                    logger.LogInformation($"{cgname} : Logging informal message at {DateTime.UtcNow}");
                    logger.LogError($"{cgname} : Logging error message at {DateTime.UtcNow}");
                    Thread.Sleep(10000); // Adjust interval as needed
                }
            });

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

            var metricThread = new Thread(() =>
            {
                Random random = new Random();
                while (true)
                {
                    long cpuUsage = random.Next(1, 101);
                    Console.WriteLine($"{cgname} : emitting metric {random}");
                    // Emit the metric with an attribute.
                    CpuUsageCounter.Add(cpuUsage, new ("resource.name", $"{cgname}"), new("resource.location", "eastus"));

                    Console.WriteLine($"Emitted CPU usage: {cpuUsage} millicores");
                    Thread.Sleep(5000); // Wait a bit between emissions
                }
            });

            // Start threads
            logThread.Start();
            metricThread.Start();

            // Keep the main thread alive
            logThread.Join();
            metricThread.Join();
        }
    }
}
