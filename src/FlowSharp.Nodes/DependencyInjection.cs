using Microsoft.Extensions.DependencyInjection;
using FlowSharp.Application.Nodes;
using FlowSharp.Nodes;

namespace Microsoft.Extensions.DependencyInjection;

public static class WorkflowNodesDependencyInjection
{
    public static IServiceCollection AddWorkflowNodes(this IServiceCollection services)
    {
        // Infrastructure/Nodes assembly'sindeki tum somut INodeType implementasyonlarini
        // singleton olarak kaydeder.
        var nodeTypes = typeof(WorkflowNodesDependencyInjection).Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false, IsClass: true }
                && typeof(INodeType).IsAssignableFrom(type));

        foreach (var type in nodeTypes)
        {
            services.AddSingleton(typeof(INodeType), type);
        }

        services.AddSingleton<INodeRegistry, NodeRegistry>();
        services.AddSingleton<INodeCatalog, NodeCatalog>();

        // Credential tipleri de node'lar gibi otomatik kesfedilir (plugin'ler ekleyebilir).
        var credentialTypes = typeof(WorkflowNodesDependencyInjection).Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false, IsClass: true }
                && typeof(FlowSharp.Application.Credentials.ICredentialType).IsAssignableFrom(type));

        foreach (var type in credentialTypes)
        {
            services.AddSingleton(typeof(FlowSharp.Application.Credentials.ICredentialType), type);
        }

        services.AddSingleton<FlowSharp.Application.Credentials.ICredentialCatalog, FlowSharp.Nodes.Credentials.CredentialCatalog>();

        // AI Agent orkestrasyonu: provider'a ozel mantik generic motorda degil burada yasar.
        services.AddScoped<FlowSharp.Application.Nodes.Agents.IAgentExecutor, FlowSharp.Nodes.Ai.Agent.SemanticKernelAgentExecutor>();

        // AI/model servis hatalarini merkezi hata ceviriciye tanit (kullanici dostu mesajlar).
        FlowSharp.Nodes.Ai.AiErrorRules.Register(FlowSharp.Application.Errors.ErrorTranslator.Default);

        // RAG: tamamen yerel/in-process embedding (gomulu ONNX). Vektor deposu Infrastructure'da.
        services.AddSingleton<FlowSharp.Application.Ai.IEmbeddingGenerator, FlowSharp.Nodes.Ai.Embeddings.LocalEmbeddingGenerator>();

        // Built-in node cevirilerini (gomulu lang/*.json) acilista yukler.
        services.AddHostedService<NodeTranslationLoader>();

        return services;
    }
}
