using Microsoft.EntityFrameworkCore;
using Serilog;
using RaymarEquipmentInventory.Models;
using RaymarEquipmentInventory.Services;
using System.Data.Odbc;
using Hangfire;
using Hangfire.Dashboard;
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

using DocumentFormat.OpenXml.Bibliography;
using Google.Apis.Drive.v3.Data;
using SoapCore;
using SoapCore.Extensibility; // if needed by your version
using System.ServiceModel.Channels;
using Microsoft.AspNetCore.HttpOverrides;
using RaymarEquipmentInventory.DTOs;
using Microsoft.Extensions.Options;
using RaymarEquipmentInventory.BackgroundEmailTasks;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog for logging
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("Logs/log.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Services.AddHttpClient();
builder.Services.AddSoapCore();

// QBWC DI bits
builder.Services.AddMemoryCache();                                            // needed by session store
builder.Services.Configure<QbwcRequestOptions>(builder.Configuration.GetSection("QBWC")); // bind options in ALL envs

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
        "GoogleDrive__TemplatesFolderId",
        "Resend_Key",
        "Receipt_Receiver_1",
        "Receipt_Receiver_2",
        "Receipt_Receiver_3",
        "WO_Receiver1",
        "WO_Receiver2",
        "WO_Receiver3",
        "IIF_AR_ACCOUNT",
        "IIF_ITEM_LABOUR",
        "IIF_ITEM_LABOUR_OT",
        "IIF_ITEM_TRAVEL_TIME",
        "IIF_ITEM_MILEAGE",
        "IIF_ITEM_MISC_PART",
        "IIF_ITEM_FEE",
        "IIF_ITEM_HST",
        "IIF_HST_RATE",
        "Upsert_ImapHost",
        "Upsert_ImapPort",
        "Upsert_Email",
        "Upsert_Password",
        "Upsert_ExpectedSubject_Inventory",
        "Upsert_ExpectedAttachment_Inventory",
        "GOOGLE_DBBackups",
        "SQL_BACKUP_CONNSTR",
        "SQL_BACKUP_PASSWORD",
        "SQL_BACKUP_USER"
    })
        SetIfPresent(k, builder.Configuration[k]);

    // Google SDK vars (come from your "Google" section)
    SetIfPresent("GOOGLE_APPLICATION_CREDENTIALS", builder.Configuration["Google:ApplicationCredentials"]);
    SetIfPresent("GOOGLE_CLOUD_PROJECT", builder.Configuration["Google:Project"]);
    SetIfPresent("GOOGLE_WORKLOAD_IDENTITY_POOL", builder.Configuration["Google:WorkloadIdentityPool"]);
    SetIfPresent("GOOGLE_WORKLOAD_IDENTITY_PROVIDER", builder.Configuration["Google:WorkloadIdentityProvider"]);
    SetIfPresent("GOOGLE_IMPERSONATE_SERVICE_ACCOUNT", builder.Configuration["Google:ImpersonateServiceAccount"]);


    var qbwc = builder.Configuration.GetSection("QBWC").Get<QbwcRequestOptions>() ?? new();
    Console.WriteLine($"QBWC bound: PageSize={qbwc.PageSize}, ActiveOnly={qbwc.ActiveOnly}, FromModified={qbwc.FromModifiedDateUtc}, FirstCompany={qbwc.RequestCompanyQueryFirst}");


    builder.Services.Configure<QbwcRequestOptions>(
        builder.Configuration.GetSection("QBWC"));
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



builder.Services.Configure<PartsInboxOptions>(opt =>
{
    opt.ImapHost = Environment.GetEnvironmentVariable("Upsert_ImapHost")!;
    opt.ImapPort = int.Parse(Environment.GetEnvironmentVariable("Upsert_ImapPort") ?? "993");
    opt.Email = Environment.GetEnvironmentVariable("Upsert_Email")!;
    opt.Password = Environment.GetEnvironmentVariable("Upsert_Password")!;
    opt.ExpectedSubject = Environment.GetEnvironmentVariable("Upsert_ExpectedSubject_Inventory") ?? "Inventory Upsert";
    opt.ExpectedAttachment = Environment.GetEnvironmentVariable("Upsert_ExpectedAttachment_Inventory") ?? "InventoryUpsert";
});

builder.Services.Configure<CustomersInboxOptions>(opt =>
{
    opt.ImapHost = Environment.GetEnvironmentVariable("Upsert_ImapHost")!;
    opt.ImapPort = int.Parse(Environment.GetEnvironmentVariable("Upsert_ImapPort") ?? "993");
    opt.Email = Environment.GetEnvironmentVariable("Upsert_Email")!;
    opt.Password = Environment.GetEnvironmentVariable("Upsert_Password")!;
    opt.ExpectedSubject = Environment.GetEnvironmentVariable("Upsert_ExpectedSubject_Customer") ?? "Customer Upsert";
    opt.ExpectedAttachment = Environment.GetEnvironmentVariable("Upsert_ExpectedAttachment_Customer") ?? "CustomerUpsert";
});

builder.Services.AddTransient<PartsInboxJob>();
builder.Services.AddTransient<CustomersInboxJob>();
// ✅ Pre-flight validation
using (var scope = builder.Services.BuildServiceProvider().CreateScope())
{
    var opts = scope.ServiceProvider.GetRequiredService<IOptions<PartsInboxOptions>>().Value;

    var missing = new List<string>();
    if (string.IsNullOrWhiteSpace(opts.ImapHost)) missing.Add("Upsert_ImapHost");
    if (opts.ImapPort <= 0) missing.Add("Upsert_ImapPort");
    if (string.IsNullOrWhiteSpace(opts.Email)) missing.Add("Upsert_Email");
    if (string.IsNullOrWhiteSpace(opts.Password)) missing.Add("Upsert_Password");

    if (missing.Count > 0)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[CONFIG ERROR] Missing required settings: {string.Join(", ", missing)}");
        Console.ResetColor();
        throw new InvalidOperationException("Critical mail settings missing.");
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[CONFIG OK] IMAP host={opts.ImapHost}, port={opts.ImapPort}, user={opts.Email}");
        Console.WriteLine($"[CONFIG OK] Expected subject={opts.ExpectedSubject}, attachment={opts.ExpectedAttachment}");
        Console.ResetColor();
    }
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

var connectionString = builder.Configuration.GetConnectionString("RaymarAzureConnection");

builder.Services.AddDbContext<RaymarInventoryDBContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<IMailService, MailService>();
builder.Services.AddScoped<IReportingService, ReportingService>();
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
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<IQBWCSessionStore, QbwcSessionStore>();
builder.Services.AddScoped<IQBWCRequestBuilder, QbwcRequestBuilder>();
builder.Services.AddScoped<IQBWCResponseHandler, QbwcResponseHandler>();
builder.Services.AddScoped<IQuickBooksConnectionService, QuickBooksConnectionService>();
builder.Services.AddScoped<IInventoryImportService, InventoryImportService>();
builder.Services.AddScoped<IInvoiceSnapshotService, InvoiceSnapshotService>();
builder.Services.AddScoped<ICustomerImportService, CustomerImportService>();
builder.Services.AddScoped<IQBWebConnectorSvc, QbwcSoapService>();
builder.Services.AddScoped<IQBItemCatalogImportService, QBItemCatalogImportService>();
builder.Services.AddScoped<IQBItemOtherImportService, QBItemOtherImportService>();
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

var serviceProvider = app.Services;

var hangfireConfig = new HangfireConfiguration(
    serviceProvider.GetRequiredService<IRecurringJobManager>()
);

hangfireConfig.InitializeJobs();



// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();


app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
});

app.UseRouting();
app.UseAuthentication();   // if used
app.UseAuthorization();

// 👇 Disambiguate by casting to IApplicationBuilder
((IApplicationBuilder)app).UseSoapEndpoint<IQBWebConnectorSvc>(
    "/qbwc",
    new SoapEncoderOptions { MessageVersion = MessageVersion.Soap11 }, // SOAP 1.1
    SoapSerializer.XmlSerializer,
    caseInsensitivePath: true
);

app.MapControllers();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new AllowAllDashboardAuthorizationFilter() }
});

app.Run();

public sealed class AllowAllDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true; // let everybody in
}
