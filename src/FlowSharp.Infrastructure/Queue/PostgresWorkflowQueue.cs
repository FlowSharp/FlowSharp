using System.Data;
using FlowSharp.Infrastructure.Data;

namespace FlowSharp.Infrastructure.Queue;

public sealed class PostgresWorkflowQueue(ApplicationDbContext dbContext) : EfWorkflowQueue(dbContext)
{
    protected override IsolationLevel DequeueIsolationLevel => IsolationLevel.ReadCommitted;
}
