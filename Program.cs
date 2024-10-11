using Microsoft.EntityFrameworkCore;
using Serilog;
using RaymarEquipmentInventory.Models;
using RaymarEquipmentInventory.Services;
using System.Data.Odbc;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using RaymarEquipmentInventory.BackgroundTasks;
using RaymarEquipmentInventory.Settings.YourApiProject.Settings;


var builder = WebApplication.CreateBuilder(args);

// Configure Serilog for logging
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("Logs/log.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

// Add Hangfire services to the container
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(builder.Configuration.GetConnectionString("RaymarAzureConnection"), new SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        QueuePollInterval = TimeSpan.Zero,
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true
    }));

var samsaraApiConfig = builder.Configuration.GetSection("SamsaraApi").Get<SamsaraApiConfig>();

builder.Services.AddSingleton(samsaraApiConfig);


// Add Hangfire server
builder.Services.AddHangfireServer();

builder.Host.UseSerilog();
// Add services to the container.

builder.Services.AddControllers();


if (samsaraApiConfig != null)
{
    builder.Services.AddHttpClient<ISamsaraApiService, SamsaraApiService>(client =>
    {
        client.BaseAddress = new Uri(samsaraApiConfig.BaseUrl);
        client.DefaultRequestHeaders.Add("accept", "application/json");
    });

}


// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();




//var quickBooksConnectionString = builder.Configuration.GetConnectionString("QuickBooksODBCConnection");
//using (OdbcConnection conn = new OdbcConnection(quickBooksConnectionString))
//{
//    try
//    {
//        conn.Open();
//        // If all goes well, nothing to see here, just a connection being opened.
//    }
//    catch (Exception ex)
//    {
//        // This is where we get to make fun of our tech error:
//        Console.WriteLine($"Oh great, another error. Apparently, QuickBooks doesn't like your connection string. Here's what it thinks: {ex.Message}");
//    }
//}


var connectionString = builder.Configuration.GetConnectionString("RaymarAzureConnection");

builder.Services.AddDbContext<RaymarInventoryDBContext>(options =>
    options.UseSqlServer(connectionString));


builder.Services.AddScoped<IBillingService, BillingService>();
builder.Services.AddScoped<ICustomerService, CustomerService>(); // Registering our new service
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IInventoryService, InventoryService>(); // Registering our new service
builder.Services.AddScoped<ILabourService, LabourService>();
builder.Services.AddScoped<IPartService, PartService>();
builder.Services.AddScoped<ITechnicianService, TechnicanService>();
builder.Services.AddScoped<IVehicleService, VehicleService>();
builder.Services.AddScoped<IWorkOrderService, WorkOrderService>();
builder.Services.AddScoped<IQuickBooksConnectionService, QuickBooksConnectionService>();


builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllOrigins",
        builder =>
        {
            builder.AllowAnyOrigin()
                   .AllowAnyHeader()
                   .AllowAnyMethod();
        });
});

builder.Host.UseWindowsService(); // This line enables running as a Windows Service
var app = builder.Build();
//app.UseCors("AllowLocalhostOrigins");
app.UseCors("AllowAllOrigins");

app.MapGet("/", () => "Welcome to Raymar Inventory API!");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("Logs/log.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();
//using (var connection = new SqlConnection(builder.Configuration.GetConnectionString("RaymarAzureConnection")))
//{
//    try
//    {
//        connection.Open();
//        Console.WriteLine("Connection successful!");
//    }
//    catch (Exception ex)
//    {
//        Console.WriteLine($"Connection failed: {ex.Message}");
//    }
//}

var serviceProvider = app.Services;
var hangfireConfig = new HangfireConfiguration(
    serviceProvider.GetRequiredService<IRecurringJobManager>(),
    serviceProvider);

hangfireConfig.InitializeJobs();

app.UseHangfireDashboard();
//app.UseHangfireServer();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
