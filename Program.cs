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
using Microsoft.AspNetCore.Http.Features;
using Google.Apis.Auth.OAuth2;
using Microsoft.Identity.Client.Platforms.Features.DesktopOs.Kerberos;
using Google.Apis.Drive.v3;
using Azure.Identity;
using Azure.Core;
using System.IO;

using Microsoft.Extensions.Logging;
using System.Threading;

using RaymarEquipmentInventory.Helpers;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog for logging
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("Logs/log.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Services.AddHttpClient();
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

// Only hydrate environment variables from config in Development
if (builder.Environment.IsDevelopment())
{
    // helper
    void SetIfPresent(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            Environment.SetEnvironmentVariable(key, value);
    }

    // Flat keys (exact names from your appsettings.Development.json)
    foreach (var k in new[]
    {
        "AZURE_CLIENT_ID",
        "AZURE_FORMRECOGNIZER_ENDPOINT",
        "AZURE_FORMRECOGNIZER_KEY",
        "AZURE_FORMRECOGNIZER_MODEL",
        "AZURE_STORAGE_CONNECTION_STRING",
        "AZURE_TENANT_ID",
        "BlobContainer_ExpenseLogs",
        "BlobContainer_Parts",
        "BlobContainer_PDFs",
        "BlobContainer_Receipts",
        "BlobStorage_ConnectionString",
        "GoogleOAuth__ClientId",
        "GoogleOAuth__ClientSecret",
        "GoogleOAuth__TokenPassword",
        "GoogleDrive__RootFolderId",
        "GoogleDrive__SharedEmail",
        "GoogleDrive__TemplatesFolderId"
    })
        SetIfPresent(k, builder.Configuration[k]);

    // Google SDK vars (come from your "Google" section)
    SetIfPresent("GOOGLE_APPLICATION_CREDENTIALS", builder.Configuration["Google:ApplicationCredentials"]);
    SetIfPresent("GOOGLE_CLOUD_PROJECT", builder.Configuration["Google:Project"]);
    SetIfPresent("GOOGLE_WORKLOAD_IDENTITY_POOL", builder.Configuration["Google:WorkloadIdentityPool"]);
    SetIfPresent("GOOGLE_WORKLOAD_IDENTITY_PROVIDER", builder.Configuration["Google:WorkloadIdentityProvider"]);
    SetIfPresent("GOOGLE_IMPERSONATE_SERVICE_ACCOUNT", builder.Configuration["Google:ImpersonateServiceAccount"]);

    // (Optional) quick sanity peek — redact secrets if you log these
    // Console.WriteLine($"BlobContainer_Receipts={Environment.GetEnvironmentVariable("BlobContainer_Receipts")}");

    // OCD confirmation pass — retrieve and store in vars
    var azureClientID = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
    var azureFormRecognizerEndpoint = Environment.GetEnvironmentVariable("AZURE_FORMRECOGNIZER_ENDPOINT");
    var azureFormRecognizerKey = Environment.GetEnvironmentVariable("AZURE_FORMRECOGNIZER_KEY");
    var azureFormRecognizerModel = Environment.GetEnvironmentVariable("AZURE_FORMRECOGNIZER_MODEL");
    var azureStorageConnectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
    var azureTenantID = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");

    var blobContainerExpenseLogs = Environment.GetEnvironmentVariable("BlobContainer_ExpenseLogs");
    var blobContainerParts = Environment.GetEnvironmentVariable("BlobContainer_Parts");
    var blobContainerPDFs = Environment.GetEnvironmentVariable("BlobContainer_PDFs");
    var blobContainerReceipts = Environment.GetEnvironmentVariable("BlobContainer_Receipts");
    var blobStorageConnectionString = Environment.GetEnvironmentVariable("BlobStorage_ConnectionString");

    var googleOAuthClientId = Environment.GetEnvironmentVariable("GoogleOAuth__ClientId");
    var googleOAuthClientSecret = Environment.GetEnvironmentVariable("GoogleOAuth__ClientSecret");
    var googleOAuthTokenPassword = Environment.GetEnvironmentVariable("GoogleOAuth__TokenPassword");
}


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
builder.Services.AddScoped<IPermissionsService, PermissionsService>();
builder.Services.AddScoped<ICustomerService, CustomerService>(); // Registering our new service
builder.Services.AddScoped<IWorkOrderFeeService, WorkOrderFeeService>(); // Registering our new service
builder.Services.AddScoped<IBillingService, BillingService>();
builder.Services.AddScoped<IHourlyLabourService, HourlyLabourService>(); // Registering our new service
builder.Services.AddScoped<IMileageAndTravelService, MileageAndTravelService>(); // Registering our new service
builder.Services.AddScoped<IDriveUploaderService, DriveUploaderService>();
builder.Services.AddScoped<IReceiptLexicon, ReceiptLexicon>();
builder.Services.AddScoped<IDriveAuthService, DriveAuthService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IInventoryService, InventoryService>(); // Registering our new service
builder.Services.AddScoped<ILabourService, LabourService>();
builder.Services.AddScoped<IPartService, PartService>();
builder.Services.AddScoped<ITechnicianService, TechnicanService>();
builder.Services.AddScoped<IVehicleService, VehicleService>();
builder.Services.AddScoped<IWorkOrderService, WorkOrderService>();
builder.Services.AddScoped<ITechWOService, TechWOService>();
builder.Services.AddScoped<IQuickBooksConnectionService, QuickBooksConnectionService>();
Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", "");
Environment.SetEnvironmentVariable("GOOGLE_CLOUD_PROJECT", "");
Environment.SetEnvironmentVariable("GOOGLE_WORKLOAD_IDENTITY_POOL", "");
Environment.SetEnvironmentVariable("GOOGLE_WORKLOAD_IDENTITY_PROVIDER", "");
Environment.SetEnvironmentVariable("GOOGLE_IMPERSONATE_SERVICE_ACCOUNT", "");

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 200_000_000; // 200MB if you want
});

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
var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";


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
    serviceProvider.GetRequiredService<IRecurringJobManager>()
);

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
