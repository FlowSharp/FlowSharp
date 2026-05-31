namespace FlowSharp.Domain.Security;

public static class AppPermissions
{
    public const string WorkflowsRead = "workflows.read";
    public const string WorkflowsWrite = "workflows.write";
    public const string WorkflowsExecute = "workflows.execute";
    public const string ExecutionsRead = "executions.read";
    public const string PluginsManage = "plugins.manage";

    public static readonly string[] All =
    [
        WorkflowsRead,
        WorkflowsWrite,
        WorkflowsExecute,
        ExecutionsRead,
        PluginsManage
    ];
}
