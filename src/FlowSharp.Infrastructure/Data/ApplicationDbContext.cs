using System.Text.Json;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using FlowSharp.Domain.Credentials;
using FlowSharp.Domain.Queue;
using FlowSharp.Domain.Triggers;
using FlowSharp.Domain.Workflows;
using FlowSharp.Infrastructure.Identity;

namespace FlowSharp.Infrastructure.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Workflow> Workflows => Set<Workflow>();

    public DbSet<WorkflowExecution> WorkflowExecutions => Set<WorkflowExecution>();

    public DbSet<WorkflowJob> WorkflowJobs => Set<WorkflowJob>();

    public DbSet<Credential> Credentials => Set<Credential>();

    public DbSet<WebhookRegistration> WebhookRegistrations => Set<WebhookRegistration>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        var jsonColumnType = GetJsonColumnType();
        var isSqlite = Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        builder.Entity<Workflow>(entity =>
        {
            entity.ToTable("workflows");
            entity.Property(workflow => workflow.Name).HasMaxLength(200).IsRequired();
            entity.Property(workflow => workflow.OwnerId).HasMaxLength(450);
            entity.HasIndex(workflow => workflow.OwnerId);
            entity.Property(workflow => workflow.Description).HasMaxLength(1000);
            entity.Property(workflow => workflow.Definition).ConfigureJsonDocument(jsonColumnType);
            entity.HasMany(workflow => workflow.Executions)
                .WithOne(execution => execution.Workflow)
                .HasForeignKey(execution => execution.WorkflowId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WorkflowExecution>(entity =>
        {
            entity.ToTable("workflow_executions");
            entity.Property(execution => execution.Input).ConfigureJsonDocument(jsonColumnType);
            entity.Property(execution => execution.Output).ConfigureJsonDocument(jsonColumnType);
            entity.Property(execution => execution.Error).HasMaxLength(4000);
            entity.HasIndex(execution => new { execution.WorkflowId, execution.Status });
        });

        builder.Entity<WorkflowJob>(entity =>
        {
            entity.ToTable("workflow_jobs");
            entity.Property(job => job.Payload).ConfigureJsonDocument(jsonColumnType);
            entity.Property(job => job.LockedBy).HasMaxLength(200);
            entity.Property(job => job.LastError).HasMaxLength(4000);
            entity.Property(job => job.DedupeKey).HasMaxLength(300);
            entity.HasIndex(job => new { job.Status, job.AvailableAt });
            entity.HasIndex(job => job.LockedUntil);
            // Idempotency: ayni anahtarla yalniz tek is. Nullable oldugundan SQL Server
            // saglayicisi otomatik olarak "DedupeKey IS NOT NULL" filtreli unique index uretir;
            // Postgres/SQLite'ta birden cok NULL zaten serbesttir (manuel enqueue'lar null kullanir).
            entity.HasIndex(job => job.DedupeKey).IsUnique();
        });

        builder.Entity<Credential>(entity =>
        {
            entity.ToTable("credentials");
            entity.Property(credential => credential.Name).HasMaxLength(200).IsRequired();
            entity.Property(credential => credential.OwnerId).HasMaxLength(450);
            entity.Property(credential => credential.Type).HasMaxLength(100).IsRequired();
            entity.Property(credential => credential.EncryptedData).IsRequired();
            // Ayni sahip altinda (tip, ad) benzersiz; farkli kullanicilar ayni adi kullanabilir.
            entity.HasIndex(credential => new { credential.OwnerId, credential.Type, credential.Name }).IsUnique();
        });

        builder.Entity<WebhookRegistration>(entity =>
        {
            entity.ToTable("webhook_registrations");
            entity.Property(registration => registration.NodeName).HasMaxLength(200).IsRequired();
            entity.Property(registration => registration.Method).HasMaxLength(10).IsRequired();
            entity.Property(registration => registration.Path).HasMaxLength(400).IsRequired();
            entity.Property(registration => registration.WorkflowKey).HasMaxLength(64);
            // Cozumleme ve workflow'a gore izolasyon: (WorkflowKey, Method, Path).
            entity.HasIndex(registration => new { registration.WorkflowKey, registration.Method, registration.Path });
            entity.HasIndex(registration => registration.WorkflowId);
        });

        if (isSqlite)
        {
            builder.ConfigureSqliteDateTimeOffset();
        }
    }

    private string GetJsonColumnType() =>
        Database.ProviderName switch
        {
            "Npgsql.EntityFrameworkCore.PostgreSQL" => "jsonb",
            "Microsoft.EntityFrameworkCore.SqlServer" => "nvarchar(max)",
            "Microsoft.EntityFrameworkCore.Sqlite" => "TEXT",
            _ => "jsonb"
        };

    internal static readonly ValueConverter<JsonDocument, string> JsonDocumentConverter = new(
        document => document.RootElement.GetRawText(),
        json => JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json));

    internal static readonly ValueComparer<JsonDocument> JsonDocumentComparer = new(
        (left, right) => JsonEquals(left, right),
        document => document.RootElement.GetRawText().GetHashCode(StringComparison.Ordinal),
        document => JsonDocument.Parse(document.RootElement.GetRawText()));

    private static bool JsonEquals(JsonDocument? left, JsonDocument? right) =>
        left is null
            ? right is null
            : right is not null && left.RootElement.GetRawText() == right.RootElement.GetRawText();

    internal static readonly ValueConverter<DateTimeOffset, long> DateTimeOffsetToLongConverter = new(
        value => value.ToUnixTimeMilliseconds(),
        value => DateTimeOffset.FromUnixTimeMilliseconds(value));

    internal static readonly ValueConverter<DateTimeOffset?, long?> NullableDateTimeOffsetToLongConverter = new(
        value => value.HasValue ? value.Value.ToUnixTimeMilliseconds() : null,
        value => value.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value) : null);
}

internal static class JsonDocumentPropertyBuilderExtensions
{
    public static void ConfigureJsonDocument(this PropertyBuilder<JsonDocument> property, string columnType)
    {
        property.HasColumnType(columnType);

        if (!columnType.Equals("jsonb", StringComparison.OrdinalIgnoreCase))
        {
            property.HasConversion(ApplicationDbContext.JsonDocumentConverter);
            property.Metadata.SetValueComparer(ApplicationDbContext.JsonDocumentComparer);
        }
    }
}

internal static class SqliteDateTimeOffsetModelBuilderExtensions
{
    public static void ConfigureSqliteDateTimeOffset(this ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(ApplicationDbContext.DateTimeOffsetToLongConverter);
                    property.SetColumnType("INTEGER");
                }
                else if (property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(ApplicationDbContext.NullableDateTimeOffsetToLongConverter);
                    property.SetColumnType("INTEGER");
                }
            }
        }
    }
}
