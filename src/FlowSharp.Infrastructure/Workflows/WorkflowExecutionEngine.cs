using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FlowSharp.Application.Errors;
using FlowSharp.Application.Nodes;
using FlowSharp.Application.Nodes.Agents;
using FlowSharp.Application.Nodes.Expressions;
using FlowSharp.Application.Workflows;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Infrastructure.Workflows;

/// <summary>
/// Workflow tanimini graf olarak yorumlayan yurutme motoru. Node'lari baglantilara
/// gore topolojik sirada calistirir, cok cikisli node'larin (IF/Switch) item'larini
/// dogru porta yonlendirir ve node bazli calisma gunlugu uretir.
/// </summary>
public sealed class WorkflowExecutionEngine(
    INodeRegistry registry,
    IExpressionEvaluator evaluator,
    IAgentExecutor agentExecutor,
    IServiceProvider services,
    ILogger<WorkflowExecutionEngine> logger) : IWorkflowExecutionEngine
{
    public async Task<WorkflowRunResult> ExecuteAsync(
        JsonElement definition,
        JsonElement triggerPayload,
        WorkflowExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new WorkflowExecutionOptions();
        var captureData = options.CaptureData;

        var nodes = ParseNodes(definition);
        var allConnections = ParseConnections(definition);
        var nodeById = nodes.ToDictionary(node => node.Id, node => node, StringComparer.Ordinal);

        // Baglantilari ana akis ve AI alt-node baglantisi olarak ayir.
        var mainConnections = new List<Connection>();
        var aiConnections = new List<Connection>();
        foreach (var connection in allConnections)
        {
            (IsAiConnection(connection, nodeById) ? aiConnections : mainConnections).Add(connection);
        }

        var incoming = BuildIncoming(mainConnections);

        var subNodeIds = aiConnections.Select(connection => connection.FromId).ToHashSet(StringComparer.Ordinal);
        var agentSubs = aiConnections
            .GroupBy(connection => connection.ToId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(connection => (Sub: nodeById.GetValueOrDefault(connection.FromId), Type: PortType(connection, nodeById)))
                    .Where(pair => pair.Sub is not null)
                    .Select(pair => (Sub: pair.Sub!, pair.Type))
                    .ToList(),
                StringComparer.Ordinal);

        var trigger = JsonNode.Parse(triggerPayload.GetRawText()) as JsonObject ?? new JsonObject();
        var triggerItems = NodeItem.FromDocument(triggerPayload);

        // Port indeksli node ciktilari: nodeId -> (port -> items)
        var portOutputs = new Dictionary<string, IReadOnlyList<IReadOnlyList<NodeItem>>>(StringComparer.Ordinal);
        // Expression'lar icin: node adi -> ilk port item'lari
        var outputsByName = new Dictionary<string, IReadOnlyList<NodeItem>>(StringComparer.OrdinalIgnoreCase);
        var runLog = new List<NodeRunData>();

        // --- Loop bolgeleri ---
        // Loop node'unun "loop" cikisindan (port 1) cikip tekrar kendisine donen alt-graf bir
        // "loop region"dir; motor bunu dis akistan ayirip her parti (batch) icin yeniden calistirir.
        var loopNodes = nodes.Where(node => node.Type.Equals(LoopType, StringComparison.OrdinalIgnoreCase)).ToList();
        var loopRegions = ComputeLoopRegions(loopNodes, mainConnections, nodeById);
        var allBodyIds = loopRegions.Values.SelectMany(region => region.BodyIds).ToHashSet(StringComparer.Ordinal);

        // Govde gather'i: back-edge'ler haric (loop node'una geri donen baglantilar girisi kirletmesin).
        var bodyConnections = mainConnections
            .Where(c => !(loopRegions.ContainsKey(c.ToId) && allBodyIds.Contains(c.FromId)))
            .ToList();
        var bodyIncoming = BuildIncoming(bodyConnections);

        // Dis akis baglantilari: loop seed (port 1), back-edge ve govde-ici baglantilar haric.
        var outerConnections = mainConnections
            .Where(c => !((loopRegions.ContainsKey(c.FromId) && c.FromPort == 1)
                          || allBodyIds.Contains(c.FromId)
                          || allBodyIds.Contains(c.ToId)))
            .ToList();
        var outerIncoming = BuildIncoming(outerConnections);
        var order = TopologicalOrder(nodes, outerConnections);

        // Calismanin baslangic (start) node'larini belirle (loop govdesi baslangic olamaz).
        var hasOutgoing = outerConnections.Select(connection => connection.FromId).ToHashSet(StringComparer.Ordinal);
        var explicitStart = options.StartNodeName
            ?? (trigger.TryGetPropertyValue("node", out var triggerNode) ? triggerNode?.ToString() : null);

        var implicitStarts = options.AllowSourceNodesWithoutTrigger
            ? nodes.Where(node => !outerIncoming.ContainsKey(node.Id) && hasOutgoing.Contains(node.Id))
            : nodes.Where(node => IsTrigger(node) && !outerIncoming.ContainsKey(node.Id) && hasOutgoing.Contains(node.Id));

        var startIds = (!string.IsNullOrEmpty(explicitStart)
                ? nodes.Where(node => node.Name.Equals(explicitStart, StringComparison.OrdinalIgnoreCase)).Select(node => node.Id)
                : implicitStarts.Select(node => node.Id))
            .Where(id => !allBodyIds.Contains(id))
            .ToHashSet(StringComparer.Ordinal);

        if (startIds.Count == 0)
        {
            return new WorkflowRunResult(false, "Workflow'u baslatacak bagli trigger yok.", BuildOutput(outputsByName, runLog, captureData), runLog);
        }

        var adjacency = outerConnections
            .GroupBy(connection => connection.FromId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(c => c.ToId).ToList(), StringComparer.Ordinal);
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new Queue<string>(startIds);
        while (frontier.Count > 0)
        {
            var id = frontier.Dequeue();
            if (!reachable.Add(id))
            {
                continue;
            }

            if (adjacency.TryGetValue(id, out var nexts))
            {
                foreach (var next in nexts)
                {
                    frontier.Enqueue(next);
                }
            }
        }

        // Tek bir node'u (loop/agent/normal) calistirir; ciktilari ve logu kaydeder.
        // recordLog=false: kalici runLog'a eklenmez (loop govde iterasyonlari icin) ama canli
        // event yine yayinlanir; boylece DB sismesi onlenir, canli izleme korunur.
        async Task<NodeRunData> RunNodeAsync(EngineNode node, IReadOnlyList<NodeItem> input, bool recordLog = true)
        {
            (NodeRunData Log, IReadOnlyList<IReadOnlyList<NodeItem>> Outputs) run;
            if (loopRegions.TryGetValue(node.Id, out var region))
            {
                run = await DriveLoopAsync(region, input);
            }
            else if (agentSubs.TryGetValue(node.Id, out var subs))
            {
                var agentStartedAt = DateTimeOffset.UtcNow;
                var request = new AgentRequest(
                    node.Type, node.Name, node.Parameters, input,
                    subs.Select(s => new AgentSubNode(s.Sub.Id, s.Sub.Type, s.Sub.Name, s.Sub.Parameters, s.Type)).ToList(),
                    (t, n, p, items) => new NodeExecutionContext(
                        t, n, p, items, outputsByName, trigger, 0, evaluator, services, _ => { }, cancellationToken, options.WorkflowId, options.ActorOwnerId),
                    async data =>
                    {
                        if (recordLog)
                        {
                            runLog.Add(data);
                        }

                        if (options.OnNodeCompleted is not null)
                        {
                            await options.OnNodeCompleted(data);
                        }
                    },
                    options.OnTextDelta,
                    options.ActorOwnerId);

                var agentResult = await agentExecutor.ExecuteAsync(request, cancellationToken);
                run = agentResult.Succeeded
                    ? (new NodeRunData(node.Id, node.Name, node.Type, NodeRunStatus.Succeeded,
                          captureData ? new JsonArray { agentResult.Item.Json.DeepClone() } : new JsonArray(),
                          null, agentStartedAt, DateTimeOffset.UtcNow, 1),
                       (IReadOnlyList<IReadOnlyList<NodeItem>>)[[agentResult.Item]])
                    : (new NodeRunData(node.Id, node.Name, node.Type, NodeRunStatus.Failed,
                          new JsonObject(), agentResult.Error, agentStartedAt, DateTimeOffset.UtcNow, 0),
                       (IReadOnlyList<IReadOnlyList<NodeItem>>)[]);
            }
            else
            {
                run = await ExecuteNodeAsync(node, input, outputsByName, trigger, cancellationToken, options.WorkflowId, options.ActorOwnerId, captureData);
            }

            if (recordLog)
            {
                runLog.Add(run.Log);
            }

            if (options.OnNodeCompleted is not null)
            {
                await options.OnNodeCompleted(run.Log);
            }

            if (run.Log.Status != NodeRunStatus.Failed)
            {
                portOutputs[node.Id] = run.Outputs;
                outputsByName[node.Name] = run.Outputs.Count > 0 ? run.Outputs[0] : [];
            }

            return run.Log;
        }

        // Loop node'unu parti parti surer: her parti "loop" portundan akar, govde calisir,
        // back-edge ile donen item'lar toplanir; bittiginde toplam "done" portundan cikar.
        async Task<(NodeRunData Log, IReadOnlyList<IReadOnlyList<NodeItem>> Outputs)> DriveLoopAsync(
            LoopRegion region, IReadOnlyList<NodeItem> input)
        {
            var startedAt = DateTimeOffset.UtcNow;
            var accumulated = new List<NodeItem>();
            var size = Math.Max(1, region.BatchSize);

            for (var offset = 0; offset < input.Count; offset += size)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = input.Skip(offset).Take(size).ToList();
                portOutputs[region.Node.Id] = [[], batch]; // port0 done (gecici bos), port1 loop = parti

                foreach (var bodyNode in region.BodyOrder)
                {
                    if (subNodeIds.Contains(bodyNode.Id))
                    {
                        continue;
                    }

                    var bodyInput = GatherInput(bodyIncoming.GetValueOrDefault(bodyNode.Id) ?? [], portOutputs);
                    if (bodyInput.Count == 0)
                    {
                        runLog.Add(Skipped(bodyNode, "Giris item'i yok; node atlandi."));
                        continue;
                    }

                    var bodyLog = await RunNodeAsync(bodyNode, bodyInput, recordLog: false);
                    if (bodyLog.Status == NodeRunStatus.Failed)
                    {
                        var fail = new NodeRunData(region.Node.Id, region.Node.Name, region.Node.Type,
                            NodeRunStatus.Failed, new JsonObject(), bodyLog.Error, startedAt, DateTimeOffset.UtcNow, 0);
                        return (fail, []);
                    }
                }

                foreach (var src in region.CollectSources)
                {
                    if (portOutputs.TryGetValue(src.FromId, out var ports) && src.FromPort < ports.Count)
                    {
                        accumulated.AddRange(ports[src.FromPort]);
                    }
                }
            }

            IReadOnlyList<IReadOnlyList<NodeItem>> outputs = [accumulated, []];
            var log = new NodeRunData(region.Node.Id, region.Node.Name, region.Node.Type,
                NodeRunStatus.Succeeded, captureData ? ToJson(accumulated) : new JsonArray(),
                null, startedAt, DateTimeOffset.UtcNow, accumulated.Count);
            return (log, outputs);
        }

        foreach (var node in order)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // AI alt-node'lari ve loop govdesi dis akista calismaz (loop node onlari surer).
            if (subNodeIds.Contains(node.Id) || allBodyIds.Contains(node.Id))
            {
                continue;
            }

            if (!reachable.Contains(node.Id))
            {
                continue;
            }

            var isStart = startIds.Contains(node.Id);
            var hasIncoming = outerIncoming.TryGetValue(node.Id, out var sources) && sources.Count > 0;

            IReadOnlyList<NodeItem> input;
            if (!hasIncoming || isStart)
            {
                input = IsTrigger(node) ? triggerItems : [NodeItem.Empty()];
            }
            else
            {
                input = GatherInput(sources!, portOutputs);
                if (input.Count == 0)
                {
                    runLog.Add(Skipped(node, "Giris item'i yok; node atlandi."));
                    continue;
                }
            }

            var log = await RunNodeAsync(node, input);
            if (log.Status == NodeRunStatus.Failed)
            {
                return new WorkflowRunResult(false, log.Error, BuildOutput(outputsByName, runLog, captureData), runLog);
            }
        }

        return new WorkflowRunResult(true, null, BuildOutput(outputsByName, runLog, captureData), runLog);
    }

    public async Task<IReadOnlyList<NodeParameterOption>> LoadOptionsAsync(
        JsonElement definition,
        string nodeId,
        string parameterKey,
        string? actorOwnerId = null,
        CancellationToken cancellationToken = default)
    {
        var nodes = ParseNodes(definition);
        var connections = ParseConnections(definition);
        var target = nodes.FirstOrDefault(n => n.Id == nodeId);
        if (target is null || registry.Find(target.Type) is not IHasDynamicOptions provider)
        {
            return [];
        }

        // Hedefin tum atalari (transitif upstream). Hedef ve onun cikis baglantilari haric tutulur.
        var incoming = BuildIncoming(connections);
        var ancestorIds = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(nodeId);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var conn in incoming.GetValueOrDefault(current) ?? [])
            {
                if (ancestorIds.Add(conn.FromId))
                {
                    stack.Push(conn.FromId);
                }
            }
        }

        // Atalardan olusan kucuk bir tanim calistir; her node'un birincil ciktisini yakala.
        var trimmedNodes = nodes.Where(n => ancestorIds.Contains(n.Id)).ToList();
        var trimmedConnections = connections.Where(c => ancestorIds.Contains(c.FromId) && ancestorIds.Contains(c.ToId)).ToList();
        var captured = new Dictionary<string, IReadOnlyList<NodeItem>>(StringComparer.Ordinal);

        if (trimmedNodes.Count > 0)
        {
            using var trimmedDoc = BuildDefinitionDocument(trimmedNodes, trimmedConnections);
            await ExecuteAsync(trimmedDoc.RootElement,
                JsonDocument.Parse("""{"source":"manual"}""").RootElement,
                new WorkflowExecutionOptions
                {
                    AllowSourceNodesWithoutTrigger = true,
                    OnNodeCompleted = data =>
                    {
                        captured[data.NodeId] = ToItems(data.Output);
                        return Task.CompletedTask;
                    }
                },
                cancellationToken);
        }

        // Hedefin girisi: dogrudan upstream'lerinin ciktilarinin birlesimi (state buradan gelir).
        var input = new List<NodeItem>();
        foreach (var conn in incoming.GetValueOrDefault(nodeId) ?? [])
        {
            if (captured.TryGetValue(conn.FromId, out var items))
            {
                input.AddRange(items);
            }
        }

        var outputsByName = captured
            .Where(pair => trimmedNodes.Any(n => n.Id == pair.Key))
            .ToDictionary(
                pair => trimmedNodes.First(n => n.Id == pair.Key).Name,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);

        var context = new NodeExecutionContext(
            target.Type, target.Name, target.Parameters,
            input.Count > 0 ? input : [NodeItem.Empty()],
            outputsByName, new JsonObject(), runIndex: 0, evaluator, services,
            _ => { }, cancellationToken, workflowId: null, actorOwnerId: actorOwnerId);

        return await provider.LoadOptionsAsync(context, parameterKey);
    }

    private static List<NodeItem> ToItems(JsonNode? output)
    {
        var items = new List<NodeItem>();
        if (output is JsonArray array)
        {
            foreach (var element in array)
            {
                if (element is JsonObject obj)
                {
                    items.Add(NodeItem.From((JsonObject)obj.DeepClone()));
                }
            }
        }

        return items;
    }

    private static JsonDocument BuildDefinitionDocument(List<EngineNode> nodes, List<Connection> connections)
    {
        var def = new JsonObject
        {
            ["nodes"] = new JsonArray(nodes.Select(n => (JsonNode)new JsonObject
            {
                ["id"] = n.Id,
                ["type"] = n.Type,
                ["name"] = n.Name,
                ["parameters"] = n.Parameters.DeepClone()
            }).ToArray()),
            ["connections"] = new JsonArray(connections.Select(c => (JsonNode)new JsonObject
            {
                ["from"] = c.FromId,
                ["fromPort"] = c.FromPort,
                ["to"] = c.ToId,
                ["toPort"] = c.ToPort
            }).ToArray())
        };

        return JsonDocument.Parse(def.ToJsonString());
    }

    private const string LoopType = "flow.loopOverItems";

    /// <summary>
    /// Loop node'larinin govde bolgelerini hesaplar: "loop" cikisindan (port 1) ulasilabilen,
    /// tekrar loop node'una donen alt-graf. Ic ice loop'larda her govde node'u en ictteki
    /// loop'a aittir.
    /// </summary>
    private static Dictionary<string, LoopRegion> ComputeLoopRegions(
        List<EngineNode> loopNodes, List<Connection> main, Dictionary<string, EngineNode> nodeById)
    {
        var regions = new Dictionary<string, LoopRegion>(StringComparer.Ordinal);
        if (loopNodes.Count == 0)
        {
            return regions;
        }

        var outByFrom = main
            .GroupBy(c => c.FromId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // Ham govde: loop'un port-1 hedeflerinden, loop'a donmeden ulasilan tum node'lar.
        var rawBody = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var loop in loopNodes)
        {
            var body = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            if (outByFrom.TryGetValue(loop.Id, out var seeds))
            {
                foreach (var c in seeds.Where(c => c.FromPort == 1))
                {
                    queue.Enqueue(c.ToId);
                }
            }

            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (id == loop.Id || !body.Add(id))
                {
                    continue;
                }

                if (outByFrom.TryGetValue(id, out var nexts))
                {
                    foreach (var c in nexts.Where(c => c.ToId != loop.Id))
                    {
                        queue.Enqueue(c.ToId);
                    }
                }
            }

            rawBody[loop.Id] = body;
        }

        foreach (var loop in loopNodes)
        {
            var body = rawBody[loop.Id];

            // Ic ice loop'larin govdesini cikar (onlar kendi loop'u tarafindan surulur; loop node'u kalir).
            var owned = new HashSet<string>(body, StringComparer.Ordinal);
            foreach (var other in loopNodes)
            {
                if (other.Id != loop.Id && body.Contains(other.Id))
                {
                    owned.ExceptWith(rawBody[other.Id]);
                }
            }

            var bodyNodes = owned.Where(nodeById.ContainsKey).Select(id => nodeById[id]).ToList();
            var innerConnections = main.Where(c => owned.Contains(c.FromId) && owned.Contains(c.ToId)).ToList();
            var bodyOrder = TopologicalOrder(bodyNodes, innerConnections);

            // Toplama kaynaklari: back-edge'ler; yoksa govde yapraklari (port 0).
            var backEdges = main.Where(c => c.ToId == loop.Id && body.Contains(c.FromId)).ToList();
            var collect = backEdges.Count > 0
                ? backEdges
                : owned.Where(id => !main.Any(c => c.FromId == id && owned.Contains(c.ToId)))
                       .Select(id => new Connection(id, 0, loop.Id, 0))
                       .ToList();

            regions[loop.Id] = new LoopRegion(loop, ParseBatchSize(loop), body, bodyOrder, collect);
        }

        return regions;
    }

    private static int ParseBatchSize(EngineNode loop) =>
        loop.Parameters.TryGetPropertyValue("batchSize", out var value) && value is not null &&
        int.TryParse(value.ToString(), out var size) && size > 0
            ? size
            : 1;

    private sealed record LoopRegion(
        EngineNode Node,
        int BatchSize,
        HashSet<string> BodyIds,
        List<EngineNode> BodyOrder,
        List<Connection> CollectSources);

    private async Task<(NodeRunData Log, IReadOnlyList<IReadOnlyList<NodeItem>> Outputs)> ExecuteNodeAsync(
        EngineNode node,
        IReadOnlyList<NodeItem> input,
        IReadOnlyDictionary<string, IReadOnlyList<NodeItem>> outputsByName,
        JsonObject trigger,
        CancellationToken cancellationToken,
        Guid? workflowId,
        string? actorOwnerId,
        bool captureData)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var nodeType = registry.Find(node.Type);

        // Henuz kodlanmamis node: veriyi oldugu gibi gecirerek akisi bozma.
        if (nodeType is null)
        {
            logger.LogInformation("Node tipi '{Type}' icin executor yok; giris cikisa gecildi.", node.Type);
            var passthrough = (IReadOnlyList<IReadOnlyList<NodeItem>>)[input];
            return (new NodeRunData(node.Id, node.Name, node.Type, NodeRunStatus.Skipped,
                captureData ? ToJson(input) : new JsonArray(), "Executor yok (placeholder).",
                startedAt, DateTimeOffset.UtcNow, input.Count), passthrough);
        }

        var logMessages = new List<string>();
        var context = new NodeExecutionContext(
            node.Type, node.Name, node.Parameters, input, outputsByName, trigger,
            runIndex: 0, evaluator, services, logMessages.Add, cancellationToken, workflowId, actorOwnerId);

        try
        {
            var result = await nodeType.ExecuteAsync(context);
            if (!result.Succeeded)
            {
                return (new NodeRunData(node.Id, node.Name, node.Type, NodeRunStatus.Failed,
                    new JsonObject(), result.Error, startedAt, DateTimeOffset.UtcNow, 0), result.Outputs);
            }

            var primary = result.Outputs.Count > 0 ? result.Outputs[0] : [];
            return (new NodeRunData(node.Id, node.Name, node.Type, NodeRunStatus.Succeeded,
                captureData ? ToJson(primary) : new JsonArray(), null, startedAt, DateTimeOffset.UtcNow, primary.Count), result.Outputs);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Node '{Name}' ({Type}) calismasi basarisiz.", node.Name, node.Type);
            return (new NodeRunData(node.Id, node.Name, node.Type, NodeRunStatus.Failed,
                new JsonObject(), exception.ToUserMessage(), startedAt, DateTimeOffset.UtcNow, 0),
                []);
        }
    }

    private IReadOnlyList<NodeItem> GatherInput(
        IReadOnlyList<Connection> sources,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<NodeItem>>> portOutputs)
    {
        var items = new List<NodeItem>();
        foreach (var source in sources)
        {
            if (portOutputs.TryGetValue(source.FromId, out var ports) && source.FromPort < ports.Count)
            {
                items.AddRange(ports[source.FromPort]);
            }
        }

        return items;
    }

    private static JsonNode BuildOutput(
        IReadOnlyDictionary<string, IReadOnlyList<NodeItem>> outputsByName,
        IReadOnlyList<NodeRunData> runLog,
        bool captureData)
    {
        // captureData=false: agir veriyi klonlama; yalniz iskeleti dondur (downstream akis
        // outputsByName referanslarini kullanir, bu sonuctan etkilenmez).
        if (!captureData)
        {
            return new JsonObject { ["main"] = new JsonArray(), ["nodes"] = new JsonObject() };
        }

        var byNode = new JsonObject();
        foreach (var pair in outputsByName)
        {
            byNode[pair.Key] = ToJson(pair.Value);
        }

        var lastSucceeded = runLog.LastOrDefault(r => r.Status == NodeRunStatus.Succeeded);
        return new JsonObject
        {
            ["main"] = lastSucceeded is not null && outputsByName.TryGetValue(lastSucceeded.NodeName, out var main)
                ? ToJson(main)
                : new JsonArray(),
            ["nodes"] = byNode
        };
    }

    private static JsonArray ToJson(IReadOnlyList<NodeItem> items)
    {
        var array = new JsonArray();
        foreach (var item in items)
        {
            array.Add(item.Json.DeepClone());
        }

        return array;
    }

    private NodeRunData Skipped(EngineNode node, string reason) =>
        new(node.Id, node.Name, node.Type, NodeRunStatus.Skipped, new JsonArray(), reason,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0);

    private bool IsTrigger(EngineNode node) =>
        registry.Find(node.Type)?.Definition.Kind == NodeKind.Trigger ||
        registry.Definitions.FirstOrDefault(d => d.Key.Equals(node.Type, StringComparison.OrdinalIgnoreCase))?.Kind == NodeKind.Trigger ||
        node.Type.Contains("trigger", StringComparison.OrdinalIgnoreCase);

    // ----- Ayristirma & graf -----

    private List<EngineNode> ParseNodes(JsonElement definition)
    {
        var result = new List<EngineNode>();
        if (!definition.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var node in nodes.EnumerateArray())
        {
            var id = node.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            var type = node.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
            if (id is null || type is null)
            {
                continue;
            }

            var name = node.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? type : type;
            var parameters = node.TryGetProperty("parameters", out var paramEl) && paramEl.ValueKind == JsonValueKind.Object
                ? JsonNode.Parse(paramEl.GetRawText()) as JsonObject ?? new JsonObject()
                : new JsonObject();

            result.Add(new EngineNode(id, type, name, parameters));
        }

        return result;
    }

    private List<Connection> ParseConnections(JsonElement definition)
    {
        var result = new List<Connection>();
        if (!definition.TryGetProperty("connections", out var connections) || connections.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var connection in connections.EnumerateArray())
        {
            var from = connection.TryGetProperty("from", out var fromEl) ? fromEl.GetString() : null;
            var to = connection.TryGetProperty("to", out var toEl) ? toEl.GetString() : null;
            if (from is null || to is null)
            {
                continue;
            }

            var fromPort = connection.TryGetProperty("fromPort", out var portEl) && portEl.ValueKind == JsonValueKind.Number
                ? portEl.GetInt32()
                : 0;
            var toPort = connection.TryGetProperty("toPort", out var toPortEl) && toPortEl.ValueKind == JsonValueKind.Number
                ? toPortEl.GetInt32()
                : 0;

            result.Add(new Connection(from, fromPort, to, toPort));
        }

        return result;
    }

    private static Dictionary<string, List<Connection>> BuildIncoming(IReadOnlyList<Connection> connections)
    {
        var map = new Dictionary<string, List<Connection>>(StringComparer.Ordinal);
        foreach (var connection in connections)
        {
            if (!map.TryGetValue(connection.ToId, out var list))
            {
                list = [];
                map[connection.ToId] = list;
            }

            list.Add(connection);
        }

        return map;
    }

    /// <summary>Kahn algoritmasiyla topolojik sira; dongu varsa kalan node'lar tanim sirasiyla eklenir.</summary>
    private static List<EngineNode> TopologicalOrder(List<EngineNode> nodes, IReadOnlyList<Connection> connections)
    {
        var indegree = nodes.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
        var adjacency = nodes.ToDictionary(n => n.Id, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var connection in connections)
        {
            if (indegree.ContainsKey(connection.ToId) && adjacency.ContainsKey(connection.FromId))
            {
                indegree[connection.ToId]++;
                adjacency[connection.FromId].Add(connection.ToId);
            }
        }

        var queue = new Queue<EngineNode>(nodes.Where(n => indegree[n.Id] == 0));
        var byId = nodes.ToDictionary(n => n.Id, n => n, StringComparer.Ordinal);
        var ordered = new List<EngineNode>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!visited.Add(node.Id))
            {
                continue;
            }

            ordered.Add(node);
            foreach (var next in adjacency[node.Id])
            {
                if (--indegree[next] == 0)
                {
                    queue.Enqueue(byId[next]);
                }
            }
        }

        // Dongudeki (topolojik siraya girmeyen) node'lari tanim sirasiyla ekle.
        foreach (var node in nodes.Where(n => !visited.Contains(n.Id)))
        {
            ordered.Add(node);
        }

        return ordered;
    }

    // ----- AI alt-node baglanti analizi (orkestrasyonun kendisi IAgentExecutor'da) -----

    private bool IsAiConnection(Connection connection, IReadOnlyDictionary<string, EngineNode> nodeById) =>
        nodeById.TryGetValue(connection.ToId, out var target) &&
        Def(target.Type) is { } def &&
        connection.ToPort >= 0 && connection.ToPort < def.InputPorts.Count &&
        def.InputPorts[connection.ToPort].Type != NodePortType.Main;

    private NodePortType PortType(Connection connection, IReadOnlyDictionary<string, EngineNode> nodeById) =>
        nodeById.TryGetValue(connection.ToId, out var target) &&
        Def(target.Type) is { } def && connection.ToPort < def.InputPorts.Count
            ? def.InputPorts[connection.ToPort].Type
            : NodePortType.Main;

    private NodeDefinition? Def(string type) => registry.Find(type)?.Definition;

    private sealed record EngineNode(string Id, string Type, string Name, JsonObject Parameters);

    private sealed record Connection(string FromId, int FromPort, string ToId, int ToPort);
}
