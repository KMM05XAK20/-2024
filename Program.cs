using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Remoting.Contexts;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Quartz;
using Quartz.Impl;

public class AccessLog
{
    public int Id { get; set; }
    public string RemoteHost { get; set; }
    public string RemoteLogname { get; set; }
    public string User { get; set; }
    public DateTime Time { get; set; }
    public string Request { get; set; }
    public int StatusCode { get; set; }
    public int BytesSent { get; set; }
    public string Referer { get; set; }
    public string UserAgent { get; set; }
}

public class LogsDbContext : DbContext
{
    public DbSet<AccessLog> AccessLogs { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql("Host=localhost;Database=logsdb;Username=yourusername;Password=yourpassword");
    }
}

public class LogParser
{
    public static AccessLog Parse(string logLine)
    {
       
        var parts = logLine.Split('127.0.0.1 ');
        if (parts.Length < 9) throw new FormatException("Invalid log line format");

        return new AccessLog
        {
            RemoteHost = parts[0],
            RemoteLogname = parts[1],
            User = parts[2],
            Time = DateTime.ParseExact(parts[3].TrimStart('['), "dd/MMM/yyyy:HH:mm:ss zzz", CultureInfo.InvariantCulture),
            Request = $"{parts[5]} {parts[6]} {parts[7]}".Trim('\"'),
            StatusCode = int.Parse(parts[8]),
            BytesSent = parts.Length > 9 ? int.Parse(parts[9]) : 0,
            Referer = parts.Length > 10 ? parts[10].Trim('\"') : null,
            UserAgent = parts.Length > 11 ? string.Join(' ', parts[11..]).Trim('\"') : null
        };
    }
}

public class LogFileProcessor
{
    private readonly IServiceProvider _serviceProvider;

    public LogFileProcessor(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task ProcessLogFileAsync(string filePath)
    {
        var lines = await File.ReadAllLinesAsync(filePath);
        var validLogs = new List<AccessLog>();

        foreach (var line in lines)
        {
            try
            {
                var log = LogParser.Parse(line);
                validLogs.Add(log);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing line: {line}. Error: {ex.Message}");
            }
        }

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LogsDbContext>();
        await dbContext.AccessLogs.AddRangeAsync(validLogs);
        await dbContext.SaveChangesAsync();
    }
}

public class LogFileJob : IJob
{
    private readonly LogFileProcessor _logFileProcessor;

    public LogFileJob(LogFileProcessor logFileProcessor)
    {
        _logFileProcessor = logFileProcessor;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var filePath = context.JobDetail.JobDataMap.GetString("filePath");
        await _logFileProcessor.ProcessLogFileAsync(filePath);
    }
}

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddDbContext<LogsDbContext>();
                services.AddSingleton<LogFileProcessor>();
                services.AddQuartz(q =>
                {
                    q.UseMicrosoftDependencyInjectionScopedJobFactory();

                    var jobKey = new JobKey("LogFileJob");

                    q.AddJob<LogFileJob>(opts => opts.WithIdentity(jobKey));

                    q.AddTrigger(opts => opts
                        .ForJob(jobKey)
                        .WithIdentity("LogFileTrigger")
                        .WithCronSchedule("0/5 * * * * ?")  // Каждые 5 минут
                        .UsingJobData("filePath", "path/to/your/logfile.log"));
                });

                services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
            })
            .Build();

        if (args.Length > 0 && args[0] == "process")
        {
            var processor = host.Services.GetRequiredService<LogFileProcessor>();
            await processor.ProcessLogFileAsync("path/to/your/logfile.log");
        }

        await host.RunAsync();
    }
}


public static class LogViewer
{
    public static void Run()
    {
        using var dbContext = new LogsDbContext();
        while (true)
        {
            Console.WriteLine("1. View logs");
            Console.WriteLine("2. Filter logs by status code");
            Console.WriteLine("3. Exit");
            var choice = Console.ReadLine();

            if (choice == "1")
            {
                var logs = dbContext.AccessLogs.ToList();
                foreach (var log in logs)
                {
                    Console.WriteLine($"{log.Time}: {log.Request} - {log.StatusCode}");
                }
            }
            else if (choice == "2")
            {
                Console.Write("Enter status code: ");
                if (int.TryParse(Console.ReadLine(), out var statusCode))
                {
                    var logs = dbContext.AccessLogs.Where(l => l.StatusCode == statusCode).ToList();
                    foreach (var log in logs)
                    {
                        Console.WriteLine($"{log.Time}: {log.Request} - {log.StatusCode}");
                    }
                }
                else
                {
                    Console.WriteLine("Invalid status code");
                }
            }
            else if (choice == "3")
            {
                break;
            }
        }
    }
}
