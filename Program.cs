using Microsoft.EntityFrameworkCore;
using Serilog;
using RaymarEquipmentInventory.Models;
using RaymarEquipmentInventory.Services;
using System.Data.Odbc;
var builder = WebApplication.CreateBuilder(args);

// Configure Serilog for logging
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("Logs/log.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();
// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();




var quickBooksConnectionString = builder.Configuration.GetConnectionString("QuickBooksODBCConnection");
using (OdbcConnection conn = new OdbcConnection(quickBooksConnectionString))
{
    try
    {
        conn.Open();
        // If all goes well, nothing to see here, just a connection being opened.
    }
    catch (Exception ex)
    {
        // This is where we get to make fun of our tech error:
        Console.WriteLine($"Oh great, another error. Apparently, QuickBooks doesn't like your connection string. Here's what it thinks: {ex.Message}");
    }
}


var connectionString = builder.Configuration.GetConnectionString("RaymarAzureConnection");

builder.Services.AddDbContext<RaymarInventoryDBContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<IInventoryService, InventoryService>(); // Registering our new service
builder.Services.AddScoped<IQuickBooksConnectionService, QuickBooksConnectionService>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
