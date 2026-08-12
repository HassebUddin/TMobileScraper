using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TMobileScraper.BackgroundServices;
using TMobileScraper.Data;
using TMobileScraper.Interfaces;
using TMobileScraper.Options;
using TMobileScraper.Repositories;
using TMobileScraper.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<TechnoDevContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("TechnoDevDbConnection")
        ?? throw new InvalidOperationException("TechnoDevDbConnection is required in appsettings.json");
    options.UseMySql(cs, ServerVersion.AutoDetect(cs));
});

builder.Services.AddDbContext<LeasingDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("LeasingDbConnection")
        ?? throw new InvalidOperationException("LeasingDbConnection is required in appsettings.json");
    options.UseMySql(cs, ServerVersion.AutoDetect(cs));
});

builder.Services.Configure<ScrapingOptions>(builder.Configuration.GetSection(ScrapingOptions.SectionName));
builder.Services.AddScoped<IScrapingWebsiteRepository, ScrapingWebsiteRepository>();
builder.Services.AddScoped<IScrapingWebsiteService, ScrapingWebsiteService>();
builder.Services.AddScoped<IEmailRepository, EmailRepository>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<TMobileCatalogBackgroundService>();

var host = builder.Build();

using var scope = host.Services.CreateScope();
var exitCode = await scope.ServiceProvider.GetRequiredService<TMobileCatalogBackgroundService>().RunAsync();
Environment.ExitCode = exitCode;
