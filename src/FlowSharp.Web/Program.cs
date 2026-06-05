using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using FlowSharp.Domain.Security;
using FlowSharp.Infrastructure;
using FlowSharp.Infrastructure.Data;
using FlowSharp.Infrastructure.Observability;
using FlowSharp.Infrastructure.Identity;
using FlowSharp.Web.Components;
using FlowSharp.Web.Components.Account;
using FlowSharp.Web.Endpoints;
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
builder.Services.AddMudServices();
builder.Services.AddScoped<FlowSharp.Web.Notifications.IUiNotifier, FlowSharp.Web.Notifications.UiNotifier>();

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
builder.Services.AddObservability(builder.Configuration, "flowsharp-web");
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Health check'ler: readiness DB baglantisini dogrular (k8s/LB probe + admin detayli rapor).
builder.Services.AddHealthChecks()
    .AddCheck<FlowSharp.Web.HealthChecks.DatabaseHealthCheck>("database", tags: ["ready"]);

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        // true: kayit sonrasi e-posta onayi zorunlu (gercek SMTP gonderici devreye girer).
        // false: e-posta dogrulamasi olmadan kayit aninda giris yapilir.
        options.SignIn.RequireConfirmedAccount =
            builder.Configuration.GetValue("Identity:RequireConfirmedAccount", true);
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddTransient<IEmailSender<ApplicationUser>, SmtpEmailSender>();

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
app.MapWebhookEndpoints();
app.MapHealthEndpoints();

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

// WebApplicationFactory'nin (entegrasyon testleri) generic argumani icin erisilebilir Program tipi.
public partial class Program;
