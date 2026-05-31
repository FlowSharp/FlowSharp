using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
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

        builder.Entity<Workflow>(entity =>
        {
            entity.ToTable("workflows");
            entity.Property(workflow => workflow.Name).HasMaxLength(200).IsRequired();
            entity.Property(workflow => workflow.Description).HasMaxLength(1000);
            entity.Property(workflow => workflow.Definition).HasColumnType("jsonb");
            entity.HasMany(workflow => workflow.Executions)
                .WithOne(execution => execution.Workflow)
                .HasForeignKey(execution => execution.WorkflowId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WorkflowExecution>(entity =>
        {
            entity.ToTable("workflow_executions");
            entity.Property(execution => execution.Input).HasColumnType("jsonb");
            entity.Property(execution => execution.Output).HasColumnType("jsonb");
            entity.Property(execution => execution.Error).HasMaxLength(4000);
            entity.HasIndex(execution => new { execution.WorkflowId, execution.Status });
        });

        builder.Entity<WorkflowJob>(entity =>
        {
            entity.ToTable("workflow_jobs");
            entity.Property(job => job.Payload).HasColumnType("jsonb");
            entity.Property(job => job.LockedBy).HasMaxLength(200);
            entity.Property(job => job.LastError).HasMaxLength(4000);
            entity.HasIndex(job => new { job.Status, job.AvailableAt });
            entity.HasIndex(job => job.LockedUntil);
        });

        builder.Entity<Credential>(entity =>
        {
            entity.ToTable("credentials");
            entity.Property(credential => credential.Name).HasMaxLength(200).IsRequired();
            entity.Property(credential => credential.Type).HasMaxLength(100).IsRequired();
            entity.Property(credential => credential.EncryptedData).IsRequired();
            entity.HasIndex(credential => new { credential.Type, credential.Name }).IsUnique();
        });

        builder.Entity<WebhookRegistration>(entity =>
        {
            entity.ToTable("webhook_registrations");
            entity.Property(registration => registration.NodeName).HasMaxLength(200).IsRequired();
            entity.Property(registration => registration.Method).HasMaxLength(10).IsRequired();
            entity.Property(registration => registration.Path).HasMaxLength(400).IsRequired();
            entity.HasIndex(registration => new { registration.Method, registration.Path });
            entity.HasIndex(registration => registration.WorkflowId);
        });
    }
}
