using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FlowSharp.Application.Abstractions;
using FlowSharp.Application.Nodes;
using FlowSharp.Application.Workflows;
using FlowSharp.Domain.Nodes;
using FlowSharp.Domain.Workflows;
using FlowSharp.Infrastructure.Data;

namespace FlowSharp.Web.Components.Pages;

public partial class WorkflowDesigner : IAsyncDisposable
{
    [Inject] public ApplicationDbContext DbContext { get; set; } = default!;
    [Inject] public INodeCatalog NodeCatalog { get; set; } = default!;
    [Inject] public ICredentialStore CredentialStore { get; set; } = default!;
    [Inject] public IWorkflowQueue Queue { get; set; } = default!;
    [Inject] public IWorkflowExecutionEngine Engine { get; set; } = default!;
    [Inject] public IWebhookRegistrar WebhookRegistrar { get; set; } = default!;
    [Inject] public IJSRuntime JS { get; set; } = default!;
    [Inject] public NavigationManager Navigation { get; set; } = default!;
    [Inject] public IWorkflowExecutionTracker Tracker { get; set; } = default!;
    [Inject] public IWorkflowEventPublisher EventPublisher { get; set; } = default!;
    [Inject] public FlowSharp.Application.Nodes.Expressions.IExpressionEvaluator Evaluator { get; set; } = default!;
    // Not: Node adi/aciklamasi NodeCatalog tarafindan zaten yerellestirilir; ayrica helper gerekmez.

    [Parameter] public Guid? WorkflowId { get; set; }
    [Parameter] public Guid? ExecutionId { get; set; }

    private bool IsReadOnly => ExecutionId.HasValue;

    private string GetIconClass(string? iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName)) return "bi-cpu";
        return iconName.ToLowerInvariant() switch
        {
            "bot" => "bi-robot",
            "email" or "envelope" => "bi-envelope",
            "postgres" or "database" => "bi-database",
            "webhook" or "trigger" => "bi-lightning-charge",
            "manual" or "play" => "bi-play-circle",
            "schedule" or "clock" => "bi-clock",
            "code" or "script" => "bi-code-slash",
            "merge" or "split" => "bi-bezier2",
            "filter" => "bi-funnel",
            "sliders" => "bi-sliders",
            "globe" => "bi-globe",
            "calculator" => "bi-calculator",
            "telegram" => "bi-telegram",
            "slack" => "bi-slack",
            "discord" => "bi-discord",
            _ => $"bi-{iconName.ToLowerInvariant()}"
        };
    }

    private string GetAiSubCategory(NodeDefinition def)
    {
        if (def.Category != NodeCategory.Ai) return "Diğer";

        if (def.IsSubNode)
        {
            var primarySubPort = def.SubOutputPorts.FirstOrDefault();
            if (primarySubPort?.Type == NodePortType.AiModel) return "AI Models (Chat Models)";
            if (primarySubPort?.Type == NodePortType.AiTool) return "AI Tools";
            if (primarySubPort?.Type == NodePortType.AiMemory) return "AI Memory";
            return "AI Sub-nodes";
        }

        if (def.Key == "ai.agent") return "AI Agents";
        return "AI Chat Nodes (Main Akış)";
    }

    private string workflowName = "New workflow";
    private string? description;
    private bool isActive = true;
    private Workflow? workflow;

    private readonly List<DesignerNode> nodes = [];
    private readonly List<DesignerConnection> connections = [];
    private readonly Dictionary<string, RunOutput> runOutputs = new();

    private string? selectedId;
    private string? openNodeId;
    private bool showPalette;
    private string? paletteSearch;
    private bool running;
    private string? toast;
    private bool toastError;

    private readonly HashSet<NodeCategory> expandedCats = [];
    private readonly HashSet<string> expandedSubCats = [];
    private bool showChat;
    private readonly List<ChatMessage> chatMessages = [];
    private string? chatInput;
    private bool chatBusy;
    private bool credAddOpen;
    private string? credAddParamKey;
    private string credAddName = "";
    private readonly List<CredField> credAddFields = [];

    private bool HasChatTrigger => nodes.Any(n => n.NodeKey == "chat.trigger");

    private IJSObjectReference? module;
    private DotNetObjectReference<WorkflowDesigner>? selfRef;
    private bool needsSync;

    private DesignerNode? openNode => nodes.FirstOrDefault(n => n.InstanceId == openNodeId);

    private NodeDefinition? Definition(string key) => NodeCatalog.Find(key);

    private IEnumerable<CredentialSummary> CredentialsFor(NodeDefinition? def) =>
        def is null ? [] : availableCredentials.Where(c => def.CredentialKeys.Contains(c.Type, StringComparer.OrdinalIgnoreCase));

    private IReadOnlyList<CredentialSummary> availableCredentials = [];

    protected override async Task OnInitializedAsync()
    {
        availableCredentials = await CredentialStore.ListAsync();
        
        if (!IsReadOnly)
        {
            Tracker.OnNodeCompleted += HandleExternalNodeCompleted;
        }

        if (WorkflowId is null) return;
        workflow = await DbContext.Workflows.FirstOrDefaultAsync(w => w.Id == WorkflowId);
        if (workflow is null) return;
        workflowName = workflow.Name;
        description = workflow.Description;
        isActive = workflow.IsActive;
        LoadDefinition(workflow.Definition.RootElement);

        if (ExecutionId.HasValue)
        {
            await LoadExecutionHistoryAsync(ExecutionId.Value);
        }
    }

    private async Task LoadExecutionHistoryAsync(Guid executionId)
    {
        var exec = await DbContext.WorkflowExecutions.AsNoTracking().FirstOrDefaultAsync(e => e.Id == executionId);
        if (exec is null) return;

        runOutputs.Clear();
        if (exec.Output is not null)
        {
            try
            {
                var root = exec.Output.RootElement;
                if (root.TryGetProperty("nodes", out var nodesProp) && nodesProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var nEl in nodesProp.EnumerateArray())
                    {
                        var nodeId = nEl.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                        if (string.IsNullOrEmpty(nodeId)) continue;

                        var status = nEl.TryGetProperty("status", out var stEl) ? stEl.GetString() ?? "" : "";
                        var itemCount = nEl.TryGetProperty("itemCount", out var icEl) && icEl.ValueKind == JsonValueKind.Number ? icEl.GetInt32() : 0;
                        var outputNode = nEl.TryGetProperty("output", out var outEl) ? outEl : (JsonElement?)null;

                        var node = nodes.FirstOrDefault(n => n.InstanceId == nodeId);
                        if (node is not null)
                        {
                            node.Status = status;
                        }

                        if (outputNode.HasValue)
                        {
                            runOutputs[nodeId] = new RunOutput(itemCount, JsonSerializer.Serialize(outputNode.Value, new JsonSerializerOptions { WriteIndented = true }));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowToast($"Geçmiş çalışma verisi yüklenirken hata oluştu: {ex.Message}", true);
            }
        }
    }

    private async Task HandleExternalNodeCompleted(Guid workflowId, NodeRunData data)
    {
        if (workflowId == WorkflowId)
        {
            await InvokeAsync(async () =>
            {
                var node = nodes.FirstOrDefault(n => n.InstanceId == data.NodeId);
                if (node is not null) node.Status = data.Status.ToString();
                runOutputs[data.NodeId] = new RunOutput(data.ItemCount,
                    data.Output.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                StateHasChanged();
                await SyncGraphAsync();
            });
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            module = await JS.InvokeAsync<IJSObjectReference>("import", "/js/designer.js");
            selfRef = DotNetObjectReference.Create(this);
            await module.InvokeVoidAsync("init", "nwf-canvas", selfRef, IsReadOnly);
            await SyncGraphAsync();
        }
        else if (needsSync)
        {
            needsSync = false;
            await SyncGraphAsync();
        }
    }

    private async Task SyncGraphAsync()
    {
        if (module is null) return;
        var graph = new
        {
            nodes = nodes.Select(n => new { id = n.InstanceId, x = n.X, y = n.Y }),
            connections = connections.Select(c => new { fromId = c.FromId, fromPort = c.FromPort, toId = c.ToId, toPort = c.ToPort, active = IsActiveEdge(c), ai = IsAiEdge(c) })
        };
        await module.InvokeVoidAsync("sync", "nwf-canvas", JsonSerializer.Serialize(graph));
    }

    private bool IsAiEdge(DesignerConnection c)
    {
        var fromNode = nodes.FirstOrDefault(n => n.InstanceId == c.FromId);
        var def = fromNode is null ? null : Definition(fromNode.NodeKey);
        return def is not null && c.FromPort < def.OutputPorts.Count && def.OutputPorts[c.FromPort].Type != NodePortType.Main;
    }

    private bool IsActiveEdge(DesignerConnection c) =>
        nodes.Any(n => n.InstanceId == c.FromId && n.Status == "Succeeded") &&
        nodes.Any(n => n.InstanceId == c.ToId && n.Status is "Succeeded" or "Running");

    private async Task Module(string fn)
    {
        if (module is not null) await module.InvokeVoidAsync(fn, "nwf-canvas");
    }

    // ---------- JS callbacks ----------
    [JSInvokable]
    public Task OnNodeMoved(string id, double x, double y)
    {
        if (IsReadOnly) return Task.CompletedTask;
        var node = nodes.FirstOrDefault(n => n.InstanceId == id);
        if (node is not null) { node.X = (int)x; node.Y = (int)y; }
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnNodeResized(string id, double width, double height)
    {
        if (IsReadOnly) return Task.CompletedTask;
        var node = nodes.FirstOrDefault(n => n.InstanceId == id);
        if (node is not null)
        {
            SetParam(node, "width", ((int)width).ToString());
            SetParam(node, "height", ((int)height).ToString());
        }
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnSelectionAreaFinished(double x, double y, double width, double height)
    {
        if (IsReadOnly) return Task.CompletedTask;

        var node = new DesignerNode
        {
            InstanceId = $"n{Guid.NewGuid():N}"[..9],
            NodeKey = "core.stickyNote",
            Name = UniqueName("Not / Grup"),
            Category = NodeCategory.Core,
            X = (int)x,
            Y = (int)y
        };
        node.Parameters["title"] = "Grup Başlığı";
        node.Parameters["notes"] = "";
        node.Parameters["color"] = "green";
        node.Parameters["width"] = ((int)width).ToString();
        node.Parameters["height"] = ((int)height).ToString();
        node.Parameters["collapsed"] = "false";

        nodes.Add(node);
        selectedId = node.InstanceId;
        needsSync = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    [JSInvokable]
    public async Task OnConnect(string fromId, int fromPort, string toId, int toPort)
    {
        if (IsReadOnly) return;
        var fromNode = nodes.FirstOrDefault(n => n.InstanceId == fromId);
        var toNode = nodes.FirstOrDefault(n => n.InstanceId == toId);
        if (fromNode is null || toNode is null) return;

        var fromDef = Definition(fromNode.NodeKey);
        var toDef = Definition(toNode.NodeKey);
        var fromType = fromDef is not null && fromPort < fromDef.OutputPorts.Count ? fromDef.OutputPorts[fromPort].Type : NodePortType.Main;
        var toType = toDef is not null && toPort < toDef.InputPorts.Count ? toDef.InputPorts[toPort].Type : NodePortType.Main;

        if (fromType != toType)
        {
            await ShowToast("Uyumsuz port tipleri baglanamaz.", true);
            return;
        }

        if (!connections.Any(c => c.FromId == fromId && c.FromPort == fromPort && c.ToId == toId && c.ToPort == toPort))
        {
            connections.Add(new DesignerConnection(fromId, fromPort, toId, toPort));
            await SyncGraphAsync();
            StateHasChanged();
        }
    }

    [JSInvokable]
    public async Task OnEdgeClick(string fromId, int fromPort, string toId, int toPort)
    {
        if (IsReadOnly) return;
        connections.RemoveAll(c => c.FromId == fromId && c.FromPort == fromPort && c.ToId == toId && c.ToPort == toPort);
        await SyncGraphAsync();
        StateHasChanged();
    }

    [JSInvokable]
    public Task OnNodeSelected(string id)
    {
        selectedId = id;
        StateHasChanged();
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnNodeOpen(string id)
    {
        openNodeId = id;
        selectedId = id;
        StateHasChanged();
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnCanvasClick()
    {
        selectedId = null;
        StateHasChanged();
        return Task.CompletedTask;
    }

    // ---------- Node ops ----------
    private void OpenPalette() { showPalette = true; openNodeId = null; }

    private void AddNode(NodeDefinition def)
    {
        var node = new DesignerNode
        {
            InstanceId = $"n{Guid.NewGuid():N}"[..9],
            NodeKey = def.Key,
            Name = UniqueName(def.DisplayName),
            Category = def.Category,
            X = 160 + nodes.Count % 4 * 60,
            Y = 120 + nodes.Count % 5 * 40
        };
        foreach (var p in def.Parameters.Where(p => p.DefaultValue is not null))
        {
            node.Parameters[p.Key] = p.DefaultValue!;
        }
        nodes.Add(node);
        selectedId = node.InstanceId;
        needsSync = true;
    }

    private string UniqueName(string baseName)
    {
        if (!nodes.Any(n => n.Name == baseName)) return baseName;
        var i = 1;
        while (nodes.Any(n => n.Name == $"{baseName} {i}")) i++;
        return $"{baseName} {i}";
    }

    private void RemoveNode(string id)
    {
        nodes.RemoveAll(n => n.InstanceId == id);
        connections.RemoveAll(c => c.FromId == id || c.ToId == id);
        if (selectedId == id) selectedId = null;
        if (openNodeId == id) openNodeId = null;
        needsSync = true;
    }

    private void RenameNode(DesignerNode node, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name)) node.Name = name;
    }

    private string GetParam(DesignerNode node, string key) =>
        node.Parameters.TryGetValue(key, out var v) ? v : string.Empty;

    private void SetParam(DesignerNode node, string key, string? value) =>
        node.Parameters[key] = value ?? string.Empty;

    /// <summary>Webhook node'unun tam cagri adresi: {base}/webhook/{path}.</summary>
    private string WebhookUrl(DesignerNode node)
    {
        var path = (GetParam(node, "path") ?? "my-webhook").Trim().Trim('/');
        return $"{Navigation.BaseUri.TrimEnd('/')}/webhook/{path}";
    }

    // Bilgisayardan secilen dosyayi { fileName, content(base64) } olarak parametreye yazar.
    private async Task OnFileSelectedAsync(DesignerNode node, string key, Microsoft.AspNetCore.Components.Forms.InputFileChangeEventArgs e)
    {
        try
        {
            var file = e.File;
            const long maxBytes = 15 * 1024 * 1024; // 15 MB
            using var ms = new MemoryStream();
            await file.OpenReadStream(maxBytes).CopyToAsync(ms);
            var payload = new JsonObject
            {
                ["fileName"] = file.Name,
                ["content"] = Convert.ToBase64String(ms.ToArray())
            };
            SetParam(node, key, payload.ToJsonString());
            await ShowToast($"'{file.Name}' yuklendi.");
        }
        catch (Exception ex)
        {
            await ShowToast($"Dosya yuklenemedi: {ex.Message}", true);
        }
    }

    // Parametredeki dosya JSON'undan dosya adini cikarir (UI'da gostermek icin).
    private static string? FileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            return JsonNode.Parse(value)?["fileName"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    private async Task CopyAsync(string text)
    {
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", text);
            await ShowToast("Adres kopyalandi.");
        }
        catch
        {
            await ShowToast("Kopyalanamadi.", true);
        }
    }

    private void ToggleActive(ChangeEventArgs e) => isActive = e.Value is true;

    // ---------- Palette ----------
    private IEnumerable<IGrouping<NodeCategory, NodeDefinition>> FilteredCatalog()
    {
        var all = NodeCatalog.GetAll().AsEnumerable();
        if (!string.IsNullOrWhiteSpace(paletteSearch))
        {
            all = all.Where(d => d.DisplayName.Contains(paletteSearch, StringComparison.OrdinalIgnoreCase)
                || d.Key.Contains(paletteSearch, StringComparison.OrdinalIgnoreCase));
        }
        return all.GroupBy(d => d.Category).OrderBy(g => g.Key);
    }

    // ---------- Save / Execute ----------
    private async Task SaveAsync()
    {
        var definition = BuildDefinition();
        if (workflow is null)
        {
            workflow = new Workflow { Name = workflowName, Description = description, IsActive = isActive, Definition = definition };
            DbContext.Workflows.Add(workflow);
        }
        else
        {
            workflow.Name = workflowName;
            workflow.Description = description;
            workflow.IsActive = isActive;
            workflow.Definition = definition;
            workflow.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await DbContext.SaveChangesAsync();
        await WebhookRegistrar.SyncAsync(workflow.Id, workflow.Definition.RootElement, isActive);
        await ShowToast("Workflow kaydedildi.");
        if (WorkflowId is null) Navigation.NavigateTo($"workflows/designer/{workflow.Id}");
    }

    private async Task ExecuteAsync()
    {
        running = true;
        foreach (var n in nodes) n.Status = "";
        runOutputs.Clear();
        StateHasChanged();

        try
        {
            var definition = BuildDefinition();
            var options = new WorkflowExecutionOptions
            {
                WorkflowId = WorkflowId,
                OnNodeCompleted = async data =>
                {
                    var node = nodes.FirstOrDefault(n => n.InstanceId == data.NodeId);
                    if (node is not null) node.Status = data.Status.ToString();
                    runOutputs[data.NodeId] = new RunOutput(data.ItemCount,
                        data.Output.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    await InvokeAsync(StateHasChanged);
                    await SyncGraphAsync();

                    if (WorkflowId.HasValue)
                    {
                        await EventPublisher.PublishNodeCompletedAsync(WorkflowId.Value, Guid.Empty, data);
                    }
                }
            };

            var result = await Engine.ExecuteAsync(definition.RootElement,
                JsonDocument.Parse("""{"source":"manual"}""").RootElement, options);

            await ShowToast(result.Succeeded ? "Calisma tamamlandi." : $"Hata: {result.Error}", !result.Succeeded);
        }
        catch (Exception ex)
        {
            await ShowToast($"Hata: {ex.Message}", true);
        }
        finally
        {
            running = false;
            StateHasChanged();
        }
    }

    private RunOutput? NodeOutput(string id) => runOutputs.TryGetValue(id, out var o) ? o : null;

    private JsonDocument BuildDefinition()
    {
        var def = new JsonObject
        {
            ["nodes"] = new JsonArray(nodes.Select(n => (JsonNode)new JsonObject
            {
                ["id"] = n.InstanceId,
                ["type"] = n.NodeKey,
                ["name"] = n.Name,
                ["category"] = n.Category.ToString(),
                ["position"] = new JsonObject { ["x"] = n.X, ["y"] = n.Y },
                ["parameters"] = BuildParameters(n)
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

    private static JsonObject BuildParameters(DesignerNode node)
    {
        var obj = new JsonObject();
        foreach (var pair in node.Parameters)
        {
            if (string.IsNullOrWhiteSpace(pair.Value)) continue;
            var value = pair.Value.Trim();
            if ((value.StartsWith('{') && value.EndsWith('}')) || (value.StartsWith('[') && value.EndsWith(']')))
            {
                try { obj[pair.Key] = JsonNode.Parse(value); continue; }
                catch (JsonException) { }
            }
            obj[pair.Key] = value;
        }
        return obj;
    }

    private void LoadDefinition(JsonElement definition)
    {
        if (definition.TryGetProperty("nodes", out var nodeItems))
        {
            foreach (var item in nodeItems.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString() ?? $"n{Guid.NewGuid():N}"[..9];
                var type = item.GetProperty("type").GetString() ?? "manual.trigger";
                var name = item.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? type : type;
                var category = item.TryGetProperty("category", out var cEl) && Enum.TryParse<NodeCategory>(cEl.GetString(), out var cat)
                    ? cat : Definition(type)?.Category ?? NodeCategory.Core;
                var x = item.TryGetProperty("position", out var pos) && pos.TryGetProperty("x", out var xv) ? xv.GetInt32() : 120;
                var y = item.TryGetProperty("position", out pos) && pos.TryGetProperty("y", out var yv) ? yv.GetInt32() : 120;

                var node = new DesignerNode { InstanceId = id, NodeKey = type, Name = name, Category = category, X = x, Y = y };
                if (item.TryGetProperty("parameters", out var prms) && prms.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in prms.EnumerateObject())
                    {
                        node.Parameters[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                            ? prop.Value.GetString() ?? "" : prop.Value.GetRawText();
                    }
                }
                nodes.Add(node);
            }
        }

        if (definition.TryGetProperty("connections", out var conns))
        {
            foreach (var item in conns.EnumerateArray())
            {
                var from = item.GetProperty("from").GetString();
                var to = item.GetProperty("to").GetString();
                var fromPort = item.TryGetProperty("fromPort", out var fp) && fp.ValueKind == JsonValueKind.Number ? fp.GetInt32() : 0;
                var toPort = item.TryGetProperty("toPort", out var tp) && tp.ValueKind == JsonValueKind.Number ? tp.GetInt32() : 0;
                if (from is not null && to is not null)
                {
                    connections.Add(new DesignerConnection(from, fromPort, to, toPort));
                }
            }
        }
    }

    private async Task ShowToast(string message, bool error = false)
    {
        toast = message; toastError = error;
        StateHasChanged();
        await Task.Delay(2600);
        toast = null;
        StateHasChanged();
    }

    // ---------- Palette (collapsible) ----------
    private bool IsCatOpen(NodeCategory c) => !string.IsNullOrWhiteSpace(paletteSearch) || expandedCats.Contains(c);
    private void ToggleCat(NodeCategory c) { if (!expandedCats.Remove(c)) expandedCats.Add(c); }
    private bool IsSubCatOpen(string subCat) => !string.IsNullOrWhiteSpace(paletteSearch) || expandedSubCats.Contains(subCat);
    private void ToggleSubCat(string subCat) { if (!expandedSubCats.Remove(subCat)) expandedSubCats.Add(subCat); }

    // ---------- NDV input/output ----------
    private void CloseNdv() { openNodeId = null; credAddOpen = false; }

    private DesignerNode? UpstreamNode(DesignerNode node)
    {
        var conn = connections.FirstOrDefault(c => c.ToId == node.InstanceId);
        return conn is null ? null : nodes.FirstOrDefault(n => n.InstanceId == conn.FromId);
    }

    private string? UpstreamName(DesignerNode node) => UpstreamNode(node)?.Name;

    private string? UpstreamOutput(DesignerNode node)
    {
        var up = UpstreamNode(node);
        return up is not null && runOutputs.TryGetValue(up.InstanceId, out var o) ? o.Json : null;
    }

    // ---------- Canli expression (ifade) onizleme/dogrulama ----------

    /// <summary>
    /// Bir parametre degerindeki <c>{{ ... }}</c> ifadelerini, mevcut INPUT verisi uzerinde
    /// degerlendirir. Ifade yoksa "none"; cozulup deger uretirse "valid" (yesil);
    /// cozulemez/null donerse "invalid" (kirmizi) durumu doner.
    /// </summary>
    private ExprPreview PreviewExpression(DesignerNode node, string? value)
    {
        if (!Evaluator.ContainsExpression(value))
        {
            return ExprPreview.None;
        }

        try
        {
            var context = BuildPreviewContext(node);
            var result = Evaluator.EvaluateToNode(value, context);
            if (result is null)
            {
                return ExprPreview.Invalid("Ifade cozulemedi (alan bulunamadi veya gecersiz).");
            }

            var text = result is JsonValue jv ? jv.ToString() : result.ToJsonString();
            return ExprPreview.Valid(text);
        }
        catch (Exception ex)
        {
            return ExprPreview.Invalid(ex.Message);
        }
    }

    private FlowSharp.Application.Nodes.Expressions.ExpressionContext BuildPreviewContext(DesignerNode node)
    {
        var outputs = new Dictionary<string, IReadOnlyList<NodeItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, run) in runOutputs)
        {
            var owner = nodes.FirstOrDefault(n => n.InstanceId == id);
            if (owner is null)
            {
                continue;
            }

            outputs[owner.Name] = ParseItems(run.Json);
        }

        NodeItem? current = null;
        var up = UpstreamNode(node);
        if (up is not null && outputs.TryGetValue(up.Name, out var upItems) && upItems.Count > 0)
        {
            current = upItems[0];
        }

        return new FlowSharp.Application.Nodes.Expressions.ExpressionContext
        {
            CurrentItem = current,
            ItemIndex = 0,
            NodeOutputs = outputs
        };
    }

    private static IReadOnlyList<NodeItem> ParseItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return NodeItem.FromDocument(doc.RootElement);
        }
        catch
        {
            return [];
        }
    }

    private sealed record ExprPreview(string State, string Message)
    {
        public static readonly ExprPreview None = new("none", "");
        public static ExprPreview Valid(string preview) => new("valid", preview);
        public static ExprPreview Invalid(string message) => new("invalid", message);
    }

    // ---------- Credential inline add ----------
    private void StartCredAdd(NodeDefinition def, string paramKey)
    {
        credAddOpen = true;
        credAddParamKey = paramKey;
        credAddName = "";
        credAddFields.Clear();
        foreach (var field in DefaultCredFields(def.CredentialKeys.FirstOrDefault()))
        {
            credAddFields.Add(new CredField { Key = field });
        }
        if (credAddFields.Count == 0) credAddFields.Add(new CredField { Key = "apiKey" });
    }

    // Yalniz gizli alanlar maskelenir; host/port/user gibi alanlar duz metin gosterilir.
    private static readonly string[] SecretFieldKeywords =
        ["password", "secret", "token", "apikey", "key", "connectionstring"];

    private static bool IsSecretField(string? key) =>
        !string.IsNullOrWhiteSpace(key) &&
        SecretFieldKeywords.Any(keyword => key.Replace("_", "").Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> DefaultCredFields(string? type) => type switch
    {
        "smtp" or "imap" => ["host", "port", "user", "password"],
        "slackApi" or "telegramApi" => ["token"],
        "openAiApi" or "googleGeminiApi" or "anthropicApi" or "groqApi" or "mistralApi" or "cohereApi" or "huggingFaceApi" or "openRouterApi" => ["apiKey"],
        "azureOpenAiApi" => ["apiKey", "endpoint", "deploymentName"],
        "ollamaApi" => ["endpoint"],
        "postgres" => ["connectionString"],
        _ => ["apiKey"]
    };

    private async Task SaveCredAddAsync(NodeDefinition def, string paramKey)
    {
        if (string.IsNullOrWhiteSpace(credAddName)) { await ShowToast("Credential adi gerekli.", true); return; }
        var type = def.CredentialKeys.FirstOrDefault() ?? "generic";
        var data = credAddFields.Where(f => !string.IsNullOrWhiteSpace(f.Key)).ToDictionary(f => f.Key, f => f.Value ?? "");
        await CredentialStore.SaveAsync(new CredentialInput(null, credAddName, type, data));
        availableCredentials = await CredentialStore.ListAsync();
        if (openNode is not null) SetParam(openNode, paramKey, credAddName);
        credAddOpen = false;
    }

    // ---------- Chat ----------
    private async Task OnChatKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await SendChatAsync();
    }

    private async Task SendChatAsync()
    {
        var message = chatInput?.Trim();
        if (string.IsNullOrEmpty(message) || chatBusy) return;

        chatMessages.Add(new ChatMessage(true, message));
        chatInput = "";
        chatBusy = true;
        StateHasChanged();

        try
        {
            var payload = JsonDocument.Parse(JsonSerializer.Serialize(new { source = "chat", chatInput = message, text = message }));
            var result = await Engine.ExecuteAsync(BuildDefinition().RootElement, payload.RootElement,
                new WorkflowExecutionOptions { WorkflowId = WorkflowId });
            chatMessages.Add(new ChatMessage(false, ExtractReply(result)));
        }
        catch (Exception ex)
        {
            chatMessages.Add(new ChatMessage(false, $"Hata: {ex.Message}"));
        }
        finally
        {
            chatBusy = false;
            StateHasChanged();
        }
    }

    private static string ExtractReply(WorkflowRunResult result)
    {
        if (!result.Succeeded) return result.Error ?? "Bir hata olustu.";
        if (result.Output is JsonObject obj && obj["main"] is JsonArray arr && arr.Count > 0 && arr[0] is JsonObject first)
        {
            if (first["output"] is not null) return first["output"]!.ToString();
            return first.ToJsonString();
        }
        return "(yanit uretilemedi)";
    }

    private static double PortTop(int index, int count) => count <= 1 ? 21 : 16 + index * 20;

    private static int BottomX(int index, int count) => (int)((index + 1) * 200.0 / (count + 1)) - 7;

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant() : name[..Math.Min(2, name.Length)].ToUpperInvariant();
    }

    public async ValueTask DisposeAsync()
    {
        Tracker.OnNodeCompleted -= HandleExternalNodeCompleted;
        try
        {
            if (module is not null)
            {
                await module.InvokeVoidAsync("dispose", "nwf-canvas");
                await module.DisposeAsync();
            }
        }
        catch { }
        selfRef?.Dispose();
    }

    private sealed class DesignerNode
    {
        public required string InstanceId { get; init; }
        public required string NodeKey { get; init; }
        public required string Name { get; set; }
        public required NodeCategory Category { get; init; }
        public int X { get; set; }
        public int Y { get; set; }
        public string Status { get; set; } = "";
        public Dictionary<string, string> Parameters { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record DesignerConnection(string FromId, int FromPort, string ToId, int ToPort);

    private sealed record RunOutput(int ItemCount, string Json);

    private sealed record ChatMessage(bool IsUser, string Text);

    private sealed class CredField
    {
        public string Key { get; set; } = "";
        public string? Value { get; set; }
    }
}
