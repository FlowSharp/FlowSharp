using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using FlowSharp.Domain.Security;
using FlowSharp.Infrastructure;
using FlowSharp.Infrastructure.Data;
using FlowSharp.Infrastructure.Identity;
using FlowSharp.Web.Components;
using FlowSharp.Web.Components.Account;
using FlowSharp.Web.Endpoints;
using FlowSharp.Web.Hubs;
using FlowSharp.Web.Localization;
using Microsoft.AspNetCore.Localization;
using Serilog;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys")));

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/web-.log", rollingInterval: RollingInterval.Day));

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSignalR();
builder.Services.AddMudServices();

// UI dili: lang/*.json. Desteklenen diller klasordeki dosyalardan turetilir.
builder.Services.AddSingleton<ILocalizer, JsonLocalizer>();
var langDir = Path.Combine(builder.Environment.ContentRootPath, "lang");
var supportedCultures = Directory.Exists(langDir)
    ? Directory.GetFiles(langDir, "*.json").Select(f => Path.GetFileNameWithoutExtension(f)).ToArray()
    : ["tr"];
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.SetDefaultCulture(JsonLocalizer.DefaultCulture);
    options.AddSupportedCultures(supportedCultures);
    options.AddSupportedUICultures(supportedCultures);
});

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.AddAuthorization(options =>
{
    foreach (var permission in AppPermissions.All)
    {
        options.AddPolicy(permission, policy => policy.RequireClaim("permission", permission));
    }
});

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

var app = builder.Build();

if (builder.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
{
    await app.MigrateDatabaseAsync();
}

if (builder.Configuration.GetValue("Seed:Enabled", true))
{
    await FlowSharp.Infrastructure.Identity.IdentitySeeder.SeedAsync(app.Services, app.Configuration);
}

// plugins/ klasorundeki topluluk node'larini derleyip yukle.
await app.Services.GetRequiredService<FlowSharp.Application.Plugins.IPluginManager>()
    .LoadAllAsync();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseRequestLocalization();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapHub<WorkflowEventsHub>("/hubs/workflows");
app.MapWebhookEndpoints();

// Dil degistirme: secilen dili cookie'ye yazar ve geri yonlendirir.
app.MapGet("/set-culture", (string culture, string? redirectUri, HttpContext ctx) =>
{
    ctx.Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
        new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });
    return Results.LocalRedirect(string.IsNullOrWhiteSpace(redirectUri) ? "/" : redirectUri);
});

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.Run();
