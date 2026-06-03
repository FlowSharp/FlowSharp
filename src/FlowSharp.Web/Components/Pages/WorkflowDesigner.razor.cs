using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using FlowSharp.Application.Errors;
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
    [Inject] public FlowSharp.Application.Credentials.ICredentialCatalog CredentialCatalog { get; set; } = default!;
    [Inject] public IWorkflowQueue Queue { get; set; } = default!;
    [Inject] public IWorkflowExecutionEngine Engine { get; set; } = default!;
    [Inject] public IWebhookRegistrar WebhookRegistrar { get; set; } = default!;
    [Inject] public IJSRuntime JS { get; set; } = default!;
    [Inject] public NavigationManager Navigation { get; set; } = default!;
    [Inject] public IWorkflowExecutionTracker Tracker { get; set; } = default!;
    [Inject] public IWorkflowEventPublisher EventPublisher { get; set; } = default!;
    [Inject] public FlowSharp.Application.Nodes.Expressions.IExpressionEvaluator Evaluator { get; set; } = default!;
    [Inject] public Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] public FlowSharp.Web.Services.IUiNotifier Notifier { get; set; } = default!;
    // Not: Node adi/aciklamasi NodeCatalog tarafindan zaten yerellestirilir; ayrica helper gerekmez.

    private string? currentUserId;
    private bool isAdmin;
    private string? webhookWorkflowKey;

    [Parameter] public Guid? WorkflowId { get; set; }
    [Parameter] public Guid? ExecutionId { get; set; }

    private bool IsReadOnly => ExecutionId.HasValue;

    // Ikon tamamen node'a aittir: node bir Bootstrap Icons adi verir (orn. "robot", "database"
    // veya dogrudan "bi-robot"). Verilmezse varsayilan gosterilir. Burada artik takma ad/switch yok.
    private const string DefaultIcon = "bi-box";

    private string GetIconClass(string? iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName)) return DefaultIcon;
        return iconName.StartsWith("bi-", StringComparison.OrdinalIgnoreCase)
            ? iconName.ToLowerInvariant()
            : $"bi-{iconName.ToLowerInvariant()}";
    }

    private static string? GetSubCategory(NodeDefinition def) => def.SubCategoryKey;

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

    private readonly HashSet<string> expandedCats = [];
    private readonly HashSet<string> expandedSubCats = [];
    private bool showChat;
    private readonly List<ChatMessage> chatMessages = [];
    private string? chatInput;
    private bool chatBusy;
    private bool ShowTyping => chatBusy &&
        (chatMessages.LastOrDefault() is not { IsUser: false } lastBot || string.IsNullOrWhiteSpace(lastBot.Text));
    private bool credAddOpen;
    private string? credAddParamKey;
    private string credAddName = "";
    private readonly List<CredField> credAddFields = [];

    private bool HasChatTrigger => nodes.Any(n => n.NodeKey == "chat.trigger");
    private bool CanRunWorkflow => TriggerStartNodes().Any();
    private bool CanSendChat => ChatStartNode() is not null;

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
        (currentUserId, isAdmin) = await FlowSharp.Web.Security.CurrentUser.ResolveAsync(AuthenticationStateProvider);
        // Dropdown yalniz oturum sahibinin credential'larini gosterir (cross-tenant isim sizmasi yok).
        availableCredentials = await CredentialStore.ListAsync(currentUserId);

        if (!IsReadOnly)
        {
            Tracker.OnNodeCompleted += HandleExternalNodeCompleted;
        }

        if (WorkflowId is null) return;
        workflow = await DbContext.Workflows.FirstOrDefaultAsync(w => w.Id == WorkflowId);
        if (workflow is null) return;

        // Sahiplik: baskasinin workflow'unu acmaya calisan kullaniciyi listeye geri yonlendir.
        if (!isAdmin && workflow.OwnerId != currentUserId)
        {
            workflow = null;
            Navigation.NavigateTo("workflows");
            return;
        }
        workflowName = workflow.Name;
        description = workflow.Description;
        isActive = workflow.IsActive;
        webhookWorkflowKey = await LoadWebhookWorkflowKeyAsync(workflow.Id);
        LoadDefinition(workflow.Definition.RootElement);

        if (ExecutionId.HasValue)
        {
            await LoadExecutionHistoryAsync(ExecutionId.Value);
        }
    }

    private async Task LoadExecutionHistoryAsync(Guid executionId)
    {
        // Yalniz bu workflow'a ait execution yuklenebilir (baska workflow'un cikti gecmisine erisimi engeller).
        var exec = await DbContext.WorkflowExecutions.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == executionId && e.WorkflowId == WorkflowId);
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
                            runOutputs[nodeId] = new RunOutput(itemCount, JsonSerializer.Serialize(outputNode.Value, DisplayJson));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowToast($"Geçmiş çalışma verisi yüklenirken hata oluştu: {ex.ToUserMessage()}", true);
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
                runOutputs[data.NodeId] = ToRunOutput(data);
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
            Category = "Core",
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
    public Task OnKeyDown(string key)
    {
        if (IsReadOnly) return Task.CompletedTask;
        if ((key == "Delete" || key == "Backspace") && !string.IsNullOrEmpty(selectedId))
        {
            RemoveNode(selectedId);
            StateHasChanged();
        }
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnNodeSelected(string id)
    {
        selectedId = id;
        StateHasChanged();
        return Task.CompletedTask;
    }

    [JSInvokable]
    public async Task OnNodeOpen(string id)
    {
        openNodeId = id;
        selectedId = id;
        dynOptions.Clear();
        dynErrors.Clear();
        dynLoading.Clear();
        StateHasChanged();

        // Dinamik secenekli parametreleri otomatik yukle (upstream'i olan node'lar icin).
        var node = openNode;
        if (node is not null && HasAncestors(node) && Definition(node.NodeKey) is { } def)
        {
            foreach (var p in def.Parameters.Where(p => p.DynamicOptions))
            {
                await LoadDynamicOptionsAsync(node, p.Key);
            }
        }
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
            Category = def.CategoryDisplayName,
            X = 160 + nodes.Count % 4 * 60,
            Y = 120 + nodes.Count % 5 * 40
        };
        ApplyDefaultParameters(node);
        nodes.Add(node);
        selectedId = node.InstanceId;
        needsSync = true;
    }

    // Isim karsilastirmasi motorun $node["Ad"] cozumuyle ayni olmali: OrdinalIgnoreCase.
    // Aksi halde "HTTP" ve "http" tasarimcida farkli gorunup motorda cakisirdi.
    private string UniqueName(string baseName, string? excludeId = null)
    {
        bool Taken(string candidate) =>
            nodes.Any(n => n.InstanceId != excludeId && string.Equals(n.Name, candidate, StringComparison.OrdinalIgnoreCase));

        if (!Taken(baseName)) return baseName;
        var i = 1;
        while (Taken($"{baseName} {i}")) i++;
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
        if (string.IsNullOrWhiteSpace(name)) return;

        // Baska bir node ayni adi (buyuk/kucuk harf duyarsiz) kullaniyorsa benzersizlestir;
        // boylece motorun isim-bazli ($node["Ad"]) cozumunde ve cikti raporunda sessiz cakisma olmaz.
        node.Name = UniqueName(name.Trim(), node.InstanceId);
        needsSync = true;
    }

    private string GetParam(DesignerNode node, string key) =>
        node.Parameters.TryGetValue(key, out var v) ? v : string.Empty;

    private void SetParam(DesignerNode node, string key, string? value) =>
        node.Parameters[key] = value ?? string.Empty;

    /// <summary>Webhook node'unun tam cagri adresi: {base}/webhook/{workflowKey}/{path}.</summary>
    private string WebhookUrl(DesignerNode node)
    {
        var path = (GetParam(node, "path") ?? "my-webhook").Trim().Trim('/');
        // workflowKey workflow'a gore izolasyon saglar; henuz uretilmediyse yer tutucu goster.
        var key = string.IsNullOrEmpty(webhookWorkflowKey) ? "{key}" : webhookWorkflowKey;
        return $"{Navigation.BaseUri.TrimEnd('/')}/webhook/{key}/{path}";
    }

    private async Task<string?> LoadWebhookWorkflowKeyAsync(Guid workflowId) =>
        await DbContext.WebhookRegistrations
            .AsNoTracking()
            .Where(registration => registration.WorkflowId == workflowId)
            .Select(registration => registration.WorkflowKey)
            .FirstOrDefaultAsync();

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
            await ShowToast($"Dosya yuklenemedi: {ex.ToUserMessage()}", true);
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
    private IEnumerable<IGrouping<string, NodeDefinition>> FilteredCatalog()
    {
        var all = NodeCatalog.GetAll().AsEnumerable();
        if (!string.IsNullOrWhiteSpace(paletteSearch))
        {
            all = all.Where(d => d.DisplayName.Contains(paletteSearch, StringComparison.OrdinalIgnoreCase)
                || d.Key.Contains(paletteSearch, StringComparison.OrdinalIgnoreCase));
        }
        return all.GroupBy(d => d.CategoryDisplayName).OrderBy(g => g.Key);
    }

    // ---------- Save / Execute ----------
    private async Task SaveAsync()
    {
        var definition = BuildDefinition();
        if (workflow is null)
        {
            workflow = new Workflow { Name = workflowName, Description = description, IsActive = isActive, Definition = definition, OwnerId = currentUserId };
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
        webhookWorkflowKey = await LoadWebhookWorkflowKeyAsync(workflow.Id);
        await ShowToast("Workflow kaydedildi.");
        if (WorkflowId is null) Navigation.NavigateTo($"workflows/designer/{workflow.Id}");
    }

    private async Task ExecuteAsync()
    {
        if (!CanRunWorkflow)
        {
            await ShowToast(L["designer.run.no_connected_trigger"], true);
            return;
        }

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
                ActorOwnerId = currentUserId,
                OnNodeCompleted = async data =>
                {
                    var node = nodes.FirstOrDefault(n => n.InstanceId == data.NodeId);
                    if (node is not null) node.Status = data.Status.ToString();
                    runOutputs[data.NodeId] = ToRunOutput(data);
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
            await ShowToast($"Hata: {ex.ToUserMessage()}", true);
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
                var category = item.TryGetProperty("category", out var cEl)
                    ? cEl.GetString() ?? Definition(type)?.CategoryDisplayName ?? "Uncategorized"
                    : Definition(type)?.CategoryDisplayName ?? "Uncategorized";
                var x = item.TryGetProperty("position", out var pos) && pos.TryGetProperty("x", out var xv) ? xv.GetInt32() : 120;
                var y = item.TryGetProperty("position", out pos) && pos.TryGetProperty("y", out var yv) ? yv.GetInt32() : 120;

                var node = new DesignerNode { InstanceId = id, NodeKey = type, Name = name, Category = category, X = x, Y = y };
                ApplyDefaultParameters(node);
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

    private void ApplyDefaultParameters(DesignerNode node)
    {
        var def = Definition(node.NodeKey);
        if (def is null)
        {
            return;
        }

        foreach (var parameter in def.Parameters.Where(parameter => parameter.DefaultValue is not null))
        {
            node.Parameters.TryAdd(parameter.Key, parameter.DefaultValue!);
        }
    }

    // Bildirimler uygulamanin geri kalaniyla tutarli olsun diye merkezi IUiNotifier (MudBlazor
    // Snackbar) uzerinden gosterilir. Boylece designer'a ozel toast'in MudBlazor overlay'leri
    // altinda gizlenmesi/kopmasi sorunu da ortadan kalkar.
    private Task ShowToast(string message, bool error = false)
    {
        if (error)
        {
            Notifier.Error(message);
        }
        else
        {
            Notifier.Success(message);
        }

        return Task.CompletedTask;
    }

    // ---------- Palette (collapsible) ----------
    private bool IsCatOpen(string c) => !string.IsNullOrWhiteSpace(paletteSearch) || expandedCats.Contains(c);
    private void ToggleCat(string c) { if (!expandedCats.Remove(c)) expandedCats.Add(c); }
    private bool IsSubCatOpen(string subCat) => !string.IsNullOrWhiteSpace(paletteSearch) || expandedSubCats.Contains(subCat);
    private void ToggleSubCat(string subCat) { if (!expandedSubCats.Remove(subCat)) expandedSubCats.Add(subCat); }

    // ---------- NDV input/output ----------
    private void CloseNdv() { openNodeId = null; credAddOpen = false; }

    private DesignerNode? UpstreamNode(DesignerNode node)
    {
        var incoming = connections.Where(c => c.ToId == node.InstanceId).ToList();
        if (incoming.Count == 0)
        {
            return null;
        }

        // Ana-akis (Main) girisini tercih et. AI alt-node baglantilari (model/tool/memory portlari)
        // veri akisinin girdisi degildir; motor da bunlari ana akistan ayirir (IsAiConnection).
        // Aksi halde girdi onizlemesi/ifade cozumu yanlislikla model node'unu kaynak alip
        // "{{$json.text}}" gibi ifadeleri cozemez ("alan bulunamadi") gosterirdi.
        var def = Definition(node.NodeKey);
        bool IsMainInput(DesignerConnection c) =>
            def is null || c.ToPort >= def.InputPorts.Count || def.InputPorts[c.ToPort].Type == NodePortType.Main;

        var conn = incoming.FirstOrDefault(IsMainInput) ?? incoming[0];
        return nodes.FirstOrDefault(n => n.InstanceId == conn.FromId);
    }

    private string? UpstreamName(DesignerNode node) => UpstreamNode(node)?.Name;

    // ---------- Dinamik parametre secenekleri (generic) ----------
    // Her parametre anahtari icin yuklenen secenekleri/durumu tutar (node'a ozel kod yok).
    private readonly Dictionary<string, List<FlowSharp.Application.Nodes.NodeParameterOption>> dynOptions = new();
    private readonly HashSet<string> dynLoading = new();
    private readonly Dictionary<string, string> dynErrors = new();

    private IEnumerable<DesignerNode> Ancestors(DesignerNode node)
    {
        var visited = new HashSet<string>();
        var stack = new Stack<DesignerNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var conn in connections.Where(c => c.ToId == current.InstanceId))
            {
                var parent = nodes.FirstOrDefault(n => n.InstanceId == conn.FromId);
                if (parent is not null && visited.Add(parent.InstanceId))
                {
                    yield return parent;
                    stack.Push(parent);
                }
            }
        }
    }

    // Bir database operasyon/table node'unun zincirinde connection node var mi?
    private bool HasUpstreamConnection(DesignerNode node) =>
        Ancestors(node).Any(n => n.NodeKey.EndsWith(".connection", StringComparison.Ordinal));

    // Bir parametrenin dinamik seceneklerini generic olarak yukler. Motor, hedef node'un
    // upstream zincirini calistirip (hedefi CALISTIRMADAN) IHasDynamicOptions'tan secenekleri doner.
    private async Task LoadDynamicOptionsAsync(DesignerNode node, string parameterKey)
    {
        dynLoading.Add(parameterKey);
        dynErrors.Remove(parameterKey);
        StateHasChanged();
        try
        {
            using var doc = BuildDefinition();
            var options = await Engine.LoadOptionsAsync(doc.RootElement, node.InstanceId, parameterKey, currentUserId);
            dynOptions[parameterKey] = options.ToList();
            if (options.Count == 0)
            {
                dynErrors[parameterKey] = "Secenek bulunamadi.";
            }
        }
        catch (Exception ex)
        {
            dynErrors[parameterKey] = ex.ToUserMessage();
        }
        finally
        {
            dynLoading.Remove(parameterKey);
            StateHasChanged();
        }
    }

    private bool HasAncestors(DesignerNode node) =>
        connections.Any(c => c.ToId == node.InstanceId);

    // Kosullu gorunurluk: ShowWhen tanimliysa, hedef alanin guncel degeri izin verilen degerlerden
    // biri degilse parametre gizlenir. Tanim yoksa her zaman gorunur.
    private bool IsParamVisible(DesignerNode node, NodeParameterDefinition p)
    {
        // Upstream'den devralinan alan (orn. override Table): bir ata node ayni alani sagliyorsa gizlenir.
        // Boylece Table'a bagli Select'te tekrar tablo sorulmaz; tek basinaysa gorunur.
        if (p.InheritsUpstream && Ancestors(node).Any(a => !string.IsNullOrWhiteSpace(GetParam(a, p.Key))))
        {
            return false;
        }

        if (p.ShowWhen is not { } cond)
        {
            return true;
        }

        var current = GetParam(node, cond.Field)
            ?? Definition(node.NodeKey)?.Parameters.FirstOrDefault(x => x.Key == cond.Field)?.DefaultValue;
        return current is not null && cond.Values.Contains(current, StringComparer.OrdinalIgnoreCase);
    }

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
            return ExprPreview.Invalid(ex.ToUserMessage());
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
        else if (IsAgentMemoryNode(node))
        {
            current = NodeItem.From(new JsonObject
            {
                ["input"] = "Agent input",
                ["text"] = "Agent input"
            });
        }

        return new FlowSharp.Application.Nodes.Expressions.ExpressionContext
        {
            CurrentItem = current,
            ItemIndex = 0,
            NodeOutputs = outputs
        };
    }

    private bool IsAgentMemoryNode(DesignerNode node)
    {
        var def = Definition(node.NodeKey);
        return def is { IsSubNode: true } &&
            def.SubOutputPorts.Any(port => port.Type == NodePortType.AiMemory);
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
    // Alanlar artik credential semasindan (ICredentialCatalog) gelir; isimden tahmin yok.
    private void StartCredAdd(NodeDefinition def, string paramKey)
    {
        credAddOpen = true;
        credAddParamKey = paramKey;
        credAddName = "";
        credAddFields.Clear();

        var type = def.CredentialKeys.FirstOrDefault();
        var schema = type is not null ? CredentialCatalog.Find(type) : null;
        if (schema is not null)
        {
            foreach (var field in schema.Fields)
            {
                credAddFields.Add(new CredField
                {
                    Key = field.Key,
                    Label = field.Label,
                    FieldType = field.Type,
                    FromSchema = true,
                    Value = field.DefaultValue
                });
            }
        }

        // Sema yoksa (bilinmeyen/generic tip) tek serbest alanla basla.
        if (credAddFields.Count == 0) credAddFields.Add(new CredField { Key = "apiKey" });
    }

    private async Task SaveCredAddAsync(NodeDefinition def, string paramKey)
    {
        if (string.IsNullOrWhiteSpace(credAddName)) { await ShowToast("Credential adi gerekli.", true); return; }
        var type = def.CredentialKeys.FirstOrDefault() ?? "generic";
        var data = credAddFields.Where(f => !string.IsNullOrWhiteSpace(f.Key)).ToDictionary(f => f.Key, f => f.Value ?? "");
        // Olusturulan credential oturum sahibine ait olur; node'a Id ile referans verilir.
        var newId = await CredentialStore.SaveAsync(new CredentialInput(null, credAddName, type, data, currentUserId));
        availableCredentials = await CredentialStore.ListAsync(currentUserId);
        if (openNode is not null) SetParam(openNode, paramKey, newId.ToString());
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

        var chatStart = ChatStartNode();
        if (chatStart is null)
        {
            chatMessages.Add(new ChatMessage(false, L["designer.chat.not_connected"]));
            chatInput = "";
            StateHasChanged();
            return;
        }

        chatMessages.Add(new ChatMessage(true, message));
        var botMessage = new ChatMessage(false, "");
        chatMessages.Add(botMessage);
        chatInput = "";
        chatBusy = true;
        foreach (var n in nodes) n.Status = "";
        runOutputs.Clear();
        StateHasChanged();

        try
        {
            var payload = JsonDocument.Parse(JsonSerializer.Serialize(new { source = "chat", chatInput = message, text = message }));
            var chatStreamEnabled = IsChatStreamEnabled();
            var options = new WorkflowExecutionOptions
            {
                WorkflowId = WorkflowId,
                ActorOwnerId = currentUserId,
                StartNodeName = chatStart.Name,
                OnTextDelta = chatStreamEnabled
                    ? async delta =>
                    {
                        await InvokeAsync(() =>
                        {
                            botMessage.Text += delta;
                            StateHasChanged();
                        });
                    }
                    : null,
                OnNodeCompleted = async data =>
                {
                    var node = nodes.FirstOrDefault(n => n.InstanceId == data.NodeId);
                    if (node is not null) node.Status = data.Status.ToString();
                    runOutputs[data.NodeId] = ToRunOutput(data);
                    await InvokeAsync(StateHasChanged);
                    await SyncGraphAsync();

                    if (WorkflowId.HasValue)
                    {
                        await EventPublisher.PublishNodeCompletedAsync(WorkflowId.Value, Guid.Empty, data);
                    }
                }
            };

            var result = await Engine.ExecuteAsync(BuildDefinition().RootElement, payload.RootElement,
                options);
            if (!result.Succeeded || string.IsNullOrWhiteSpace(botMessage.Text))
            {
                botMessage.Text = ExtractReply(result);
            }
        }
        catch (Exception ex)
        {
            botMessage.Text = $"Hata: {ex.ToUserMessage()}";
        }
        finally
        {
            chatBusy = false;
            StateHasChanged();
        }
    }

    private bool IsChatStreamEnabled()
    {
        var trigger = ChatStartNode();
        if (trigger is null)
        {
            return true;
        }

        return !trigger.Parameters.TryGetValue("chatStream", out var value) ||
            !bool.TryParse(value, out var enabled) ||
            enabled;
    }

    private DesignerNode? ChatStartNode() =>
        nodes.FirstOrDefault(node =>
            node.NodeKey == "chat.trigger" &&
            connections.Any(connection => connection.FromId == node.InstanceId && IsMainOutput(node, connection.FromPort)));

    private IEnumerable<DesignerNode> TriggerStartNodes() =>
        nodes.Where(node =>
            Definition(node.NodeKey)?.Kind == NodeKind.Trigger &&
            !connections.Any(connection => connection.ToId == node.InstanceId) &&
            connections.Any(connection => connection.FromId == node.InstanceId && IsMainOutput(node, connection.FromPort)));

    private bool IsMainOutput(DesignerNode node, int port)
    {
        var def = Definition(node.NodeKey);
        return def is null || port >= def.OutputPorts.Count || def.OutputPorts[port].Type == NodePortType.Main;
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
        public required string Category { get; init; }
        public int X { get; set; }
        public int Y { get; set; }
        public string Status { get; set; } = "";
        public Dictionary<string, string> Parameters { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record DesignerConnection(string FromId, int FromPort, string ToId, int ToPort);

    // Gosterim icin JSON: Unicode'u (Turkce dahil) escape etmeden, okunabilir yazar.
    private static readonly JsonSerializerOptions DisplayJson = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static RunOutput ToRunOutput(NodeRunData data) =>
        new(data.ItemCount,
            data.Output.ToJsonString(DisplayJson),
            data.Status == NodeRunStatus.Failed ? data.Error : null);

    private sealed record RunOutput(int ItemCount, string Json, string? Error = null);

    private sealed class ChatMessage(bool isUser, string text)
    {
        public bool IsUser { get; } = isUser;
        public string Text { get; set; } = text;
    }

    private sealed class CredField
    {
        public string Key { get; set; } = "";
        public string? Value { get; set; }
        // Sema'dan gelen alanlarda render tipini ve etiketini tasir; serbest alanlarda String/key.
        public string? Label { get; set; }
        public FlowSharp.Domain.Credentials.CredentialFieldType FieldType { get; set; }
            = FlowSharp.Domain.Credentials.CredentialFieldType.String;
        public bool FromSchema { get; set; }
    }
}
