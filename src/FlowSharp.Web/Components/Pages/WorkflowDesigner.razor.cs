using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using FlowSharp.Application.Errors;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowSharp.Application.Abstractions;
using FlowSharp.Application.Json;
using FlowSharp.Application.Nodes;
using FlowSharp.Application.Security;
using FlowSharp.Application.Workflows;
using FlowSharp.Domain.Nodes;
using FlowSharp.Domain.Workflows;

namespace FlowSharp.Web.Components.Pages;

public partial class WorkflowDesigner : IAsyncDisposable
{
    [Inject] public IWorkflowService WorkflowService { get; set; } = default!;
    [Inject] public IExecutionService ExecutionService { get; set; } = default!;
    [Inject] public INodeCatalog NodeCatalog { get; set; } = default!;
    [Inject] public ICredentialStore CredentialStore { get; set; } = default!;
    [Inject] public FlowSharp.Application.Credentials.ICredentialCatalog CredentialCatalog { get; set; } = default!;
    [Inject] public IWorkflowExecutionEngine Engine { get; set; } = default!;
    [Inject] public IJSRuntime JS { get; set; } = default!;
    [Inject] public NavigationManager Navigation { get; set; } = default!;
    [Inject] public IWorkflowExecutionTracker Tracker { get; set; } = default!;
    [Inject] public IWorkflowEventPublisher EventPublisher { get; set; } = default!;
    [Inject] public FlowSharp.Application.Nodes.Expressions.IExpressionEvaluator Evaluator { get; set; } = default!;
    [Inject] public Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] public FlowSharp.Web.Notifications.IUiNotifier Notifier { get; set; } = default!;
    // Not: Node adi/aciklamasi NodeCatalog tarafindan zaten yerellestirilir; ayrica helper gerekmez.

    private string? currentUserId;
    private bool isAdmin;
    private ActorScope actor;
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
    // Cok-cikisli node'larin port-bazli son ciktilari (nodeId -> port indeksi -> JSON). Editorde
    // her dalin kendi portunun verisini gostermek icin (port-aware girdi onizlemesi).
    private readonly Dictionary<string, Dictionary<int, string>> runPortOutputs = new();

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
    private string? colorPickerNodeId; // Acik olan sticky note renk secici (yer kazanmak icin popover).
    private bool credAddOpen;
    private string? credAddParamKey;
    private string credAddName = "";
    private readonly List<CredField> credAddFields = [];

    // db.ensureTable "columns" parametresi icin satir-tabanli editor durumu (JSON'a serilesir).
    private sealed class EnsureColumn
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "TEXT";
        public bool Pk { get; set; }
        public bool NotNull { get; set; }
    }

    private readonly List<EnsureColumn> ensureColumns = [];

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
        actor = new ActorScope(currentUserId, isAdmin);
        // Dropdown yalniz oturum sahibinin credential'larini gosterir (cross-tenant isim sizmasi yok).
        availableCredentials = await CredentialStore.ListAsync(currentUserId);

        if (!IsReadOnly)
        {
            Tracker.OnNodeCompleted += HandleExternalNodeCompleted;
        }

        if (WorkflowId is null) return;

        // Sahiplik dogrulamasi servis icinde yapilir; erisim yoksa (yok/baskasinin) listeye don.
        workflow = await WorkflowService.GetForEditAsync(WorkflowId.Value, actor);
        if (workflow is null)
        {
            Navigation.NavigateTo("workflows");
            return;
        }

        workflowName = workflow.Name;
        description = workflow.Description;
        isActive = workflow.IsActive;
        webhookWorkflowKey = await WorkflowService.GetWebhookKeyAsync(workflow.Id);
        LoadDefinition(workflow.Definition.RootElement);

        if (ExecutionId.HasValue)
        {
            await LoadExecutionHistoryAsync(ExecutionId.Value);
        }
    }

    private async Task LoadExecutionHistoryAsync(Guid executionId)
    {
        // Yalniz bu workflow'a ait execution yuklenebilir (baska workflow'un cikti gecmisine erisimi engeller).
        var exec = await ExecutionService.GetForWorkflowAsync(executionId, WorkflowId!.Value);
        if (exec is null) return;

        runOutputs.Clear();
        runPortOutputs.Clear();
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
                StoreRunOutput(data);
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
        insertTarget = null;
        inputSelectedNodeId = node is not null ? UpstreamNode(node)?.InstanceId : null;

        if (node is not null && node.NodeKey == "db.ensureTable")
        {
            LoadEnsureColumns(node);
        }

        if (node is not null && IsColumnMapNode(node.NodeKey))
        {
            LoadColumnValues(node);
            if (HasAncestors(node))
            {
                await LoadDynamicOptionsAsync(node, "columnsJson");
            }
        }

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
        colorPickerNodeId = null;
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

    // "columns" JSON'unu satir editorune yukler (node acilisinda cagrilir).
    private void LoadEnsureColumns(DesignerNode node)
    {
        ensureColumns.Clear();
        var raw = GetParam(node, "columns");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(raw) is System.Text.Json.Nodes.JsonArray arr)
            {
                foreach (var item in arr.OfType<System.Text.Json.Nodes.JsonObject>())
                {
                    ensureColumns.Add(new EnsureColumn
                    {
                        Name = item["name"]?.ToString() ?? "",
                        Type = item["type"]?.ToString() ?? "TEXT",
                        Pk = EnsureBool(item["pk"]),
                        NotNull = EnsureBool(item["notnull"])
                    });
                }
            }
        }
        catch
        {
            // Bozuk JSON ise bos editorle baslar; kullanici yeniden tanimlar.
        }
    }

    private static bool EnsureBool(System.Text.Json.Nodes.JsonNode? node) =>
        node is System.Text.Json.Nodes.JsonValue v &&
        (v.TryGetValue<bool>(out var b) && b ||
         string.Equals(v.ToString(), "true", StringComparison.OrdinalIgnoreCase));

    // Satir editorunu "columns" JSON parametresine geri yazar.
    private void SyncEnsureColumns(DesignerNode node)
    {
        var arr = new System.Text.Json.Nodes.JsonArray();
        foreach (var c in ensureColumns)
        {
            if (string.IsNullOrWhiteSpace(c.Name))
            {
                continue;
            }

            var o = new System.Text.Json.Nodes.JsonObject
            {
                ["name"] = c.Name.Trim(),
                ["type"] = string.IsNullOrWhiteSpace(c.Type) ? "TEXT" : c.Type.Trim()
            };
            if (c.Pk)
            {
                o["pk"] = true;
            }

            if (c.NotNull)
            {
                o["notnull"] = true;
            }

            arr.Add(o);
        }

        SetParam(node, "columns", arr.ToJsonString());
    }

    // Sticky note'u kuculur/acar ve canvas'i yeniden senkronlar; boylece designer.js icindeki
    // node'lari ve gizler/gosterir (bagli kenarlar yalniz gizlenir, silinmez).
    private void ToggleStickyCollapse(DesignerNode node)
    {
        SetParam(node, "collapsed", GetParam(node, "collapsed") == "true" ? "false" : "true");
        needsSync = true;
    }

    /// <summary>MultiSelect CSV degerini secili oge kumesine cevirir.</summary>
    private static HashSet<string> MultiSelectValues(string? csv) =>
        (csv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// MultiSelect'te bir secenegi acar/kapatir ve CSV'yi <paramref name="options"/> sirasinda saklar.
    /// Boylece dinamik cikis port siralamasi kararli kalir.
    /// </summary>
    private void ToggleMultiSelect(DesignerNode node, string key, IReadOnlyList<string> options, string option, bool selected)
    {
        var current = MultiSelectValues(GetParam(node, key));
        if (selected)
        {
            current.Add(option);
        }
        else
        {
            current.Remove(option);
        }

        SetParam(node, key, string.Join(",", options.Where(current.Contains)));
        StateHasChanged(); // Cikis portlari degisebilir; canvas'i yeniden ciz.
    }

    /// <summary>Webhook node'unun tam cagri adresi: {base}/webhook/{workflowKey}/{path}.</summary>
    private string WebhookUrl(DesignerNode node)
    {
        var path = (GetParam(node, "path") ?? "my-webhook").Trim().Trim('/');
        // workflowKey workflow'a gore izolasyon saglar; henuz uretilmediyse yer tutucu goster.
        var key = string.IsNullOrEmpty(webhookWorkflowKey) ? "{key}" : webhookWorkflowKey;
        return $"{Navigation.BaseUri.TrimEnd('/')}/webhook/{key}/{path}";
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
        NormalizeJsonParameters(); // JSON alanlarini kaydederken okunabilir hale getir (beautify).
        var input = new WorkflowSaveInput(WorkflowId, workflowName, description, isActive, BuildDefinition());

        // Sahiplik dogrulamasi, kalici kayit ve webhook senkronu servis icinde tek noktada yapilir.
        var saved = await WorkflowService.SaveAsync(input, actor);
        webhookWorkflowKey = saved.WebhookKey;
        await ShowToast("Workflow kaydedildi.");
        if (WorkflowId is null) Navigation.NavigateTo($"workflows/designer/{saved.Id}");
    }

    private Task ExecuteAsync() => RunAsync(null);

    // Kismi yurutme: bir node ve atalari calisir (Execute node). upToNodeId null ise tam calistirma.
    private async Task ExecuteUpToAsync(DesignerNode node) => await RunAsync(node.InstanceId);

    private async Task RunAsync(string? upToNodeId)
    {
        // Tam calistirmada bagli trigger sart; kismi yurutmede hedefin atalarindan baslanir (trigger sarti yok).
        if (upToNodeId is null && !CanRunWorkflow)
        {
            await ShowToast(L["designer.run.no_connected_trigger"], true);
            return;
        }

        running = true;
        foreach (var n in nodes) n.Status = "";
        runOutputs.Clear();
        runPortOutputs.Clear();
        StateHasChanged();

        try
        {
            var definition = BuildDefinition();
            var options = new WorkflowExecutionOptions
            {
                WorkflowId = WorkflowId,
                ActorOwnerId = currentUserId,
                UpToNodeId = upToNodeId,
                OnNodeCompleted = async data =>
                {
                    var node = nodes.FirstOrDefault(n => n.InstanceId == data.NodeId);
                    if (node is not null) node.Status = data.Status.ToString();
                    StoreRunOutput(data);
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

    // ---------- Pin data: node'a sabit ornek cikti tanimla (motor calistirmadan bu veriyi kullanir) ----------
    private const string PinnedKey = "__pinned";

    private bool IsPinned(DesignerNode node) => !string.IsNullOrWhiteSpace(GetParam(node, PinnedKey));

    // Node'un son calisma ciktisini pin'ler (sabitler). Sonraki calismalarda node calistirilmaz.
    private void PinNode(DesignerNode node)
    {
        if (NodeOutput(node.InstanceId) is { Error: null } output && !string.IsNullOrWhiteSpace(output.Json))
        {
            SetParam(node, PinnedKey, output.Json);
        }
    }

    private void UnpinNode(DesignerNode node) => SetParam(node, PinnedKey, string.Empty);

    /// <summary>
    /// Bir node'un calisma ciktisini editor onizlemesi icin saklar. Cok-cikisli trigger'larda bir
    /// olay (orn. WhatsApp status) ilgili portu bos calistirir; bu bos sonuc, onceki dolu ornegin
    /// (orn. son gelen mesaj) uzerine YAZMAZ. Boylece ifade kurarken hep son anlamli veri gorunur
    /// (n8n'in "son veriyi tut" davranisi). Hata sonuclari her zaman yazilir.
    /// </summary>
    private void StoreRunOutput(NodeRunData data)
    {
        var output = ToRunOutput(data);
        if (output.ItemCount == 0 && output.Error is null
            && runOutputs.TryGetValue(data.NodeId, out var existing)
            && existing is { ItemCount: > 0, Error: null })
        {
            // Onceki dolu ornegi koru (port 0). Yine de dolu portlari guncelle (asagida).
        }
        else
        {
            runOutputs[data.NodeId] = output;
        }

        // Cok-cikisli node: her portu ayri sakla; bos port onceki dolu ornegin uzerine yazmaz.
        if (data.PortOutputs is { Count: > 0 })
        {
            var ports = runPortOutputs.TryGetValue(data.NodeId, out var existingPorts) ? existingPorts : new();
            for (var i = 0; i < data.PortOutputs.Count; i++)
            {
                var json = data.PortOutputs[i];
                var isEmpty = json is JsonArray { Count: 0 } or null;
                if (isEmpty && ports.ContainsKey(i))
                {
                    continue; // Bu portun onceki dolu ornegini koru.
                }

                ports[i] = json?.ToJsonString(DisplayJson) ?? "[]";
            }

            runPortOutputs[data.NodeId] = ports;
        }
    }

    /// <summary>Tum node'lardaki Json tipli parametreleri tek helper'dan (FlowJson) bicimler.</summary>
    private void NormalizeJsonParameters()
    {
        foreach (var node in nodes)
        {
            var def = Definition(node.NodeKey);
            if (def is null)
            {
                continue;
            }

            foreach (var p in def.Parameters.Where(p => p.Type == NodeParameterType.Json))
            {
                if (node.Parameters.TryGetValue(p.Key, out var value))
                {
                    node.Parameters[p.Key] = FlowJson.Beautify(value);
                }
            }
        }
    }

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

    private DesignerConnection? UpstreamConnection(DesignerNode node)
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

        return incoming.FirstOrDefault(IsMainInput) ?? incoming[0];
    }

    private DesignerNode? UpstreamNode(DesignerNode node)
    {
        var conn = UpstreamConnection(node);
        return conn is null ? null : nodes.FirstOrDefault(n => n.InstanceId == conn.FromId);
    }


    /// <summary>
    /// Bir node'un girdisini, bagli oldugu KAYNAK PORTUN son ciktisi olarak doner (port-aware).
    /// Cok-cikisli upstream'lerde (orn. WhatsApp Trigger Messages/Statuses) dogru dalin verisini verir;
    /// port-bazli veri yoksa port 0'a (genel cikti) duser.
    /// </summary>
    private string? UpstreamPortJson(DesignerNode node)
    {
        var conn = UpstreamConnection(node);
        if (conn is null)
        {
            return null;
        }

        if (runPortOutputs.TryGetValue(conn.FromId, out var ports) && ports.TryGetValue(conn.FromPort, out var portJson))
        {
            return portJson;
        }

        return runOutputs.TryGetValue(conn.FromId, out var o) ? o.Json : null;
    }

    // ---------- INPUT veri tarayicisi (n8n tarzi): onceki node'lar + tikla-ekle ----------
    private string? inputSelectedNodeId;
    // Son odaklanilan girisin (parametre VEYA kolon hucresi) sonuna ifade ekleyen eylem.
    private Action<string>? insertTarget;

    // Bir parametre alanina ifade ekleyen kapanis (closure) uretir.
    private Action<string> AppendToParam(string key) =>
        expr => { if (openNode is { } n) SetParam(n, key, GetParam(n, key) + expr); };

    // Bir kolon-deger hucresine ifade ekleyen kapanis uretir (columnsJson editoru).
    private Action<string> AppendToColumn(string column) =>
        expr =>
        {
            columnValues[column] = (columnValues.TryGetValue(column, out var v) ? v : "") + expr;
            if (openNode is { } n) SyncColumnValues(n);
        };

    // INPUT panelinde gosterilecek: ciktisi olan tum ata (ancestor) node'lar (yakindan uzaga).
    private List<DesignerNode> InputSourceNodes(DesignerNode node) =>
        Ancestors(node).Where(a => runOutputs.ContainsKey(a.InstanceId)).ToList();

    private sealed record InputField(int Depth, string Label, string Path, string Preview);

    // Secili kaynak node'un ilk item'inin JSON'unu duz bir alan listesine cevirir (path + onizleme).
    private List<InputField> InputFields(string? nodeId)
    {
        var fields = new List<InputField>();
        if (nodeId is null || !runOutputs.TryGetValue(nodeId, out var ro))
        {
            return fields;
        }

        System.Text.Json.Nodes.JsonNode? parsed;
        try { parsed = System.Text.Json.Nodes.JsonNode.Parse(ro.Json); }
        catch { return fields; }

        // Cikti bir item dizisidir; ilk item'i kok alir (ifade: $node["Ad"].json...).
        var root = parsed is System.Text.Json.Nodes.JsonArray arr ? (arr.Count > 0 ? arr[0] : null) : parsed;
        if (root is not null)
        {
            FlattenInput(root, "", 0, fields);
        }

        return fields;
    }

    private static void FlattenInput(System.Text.Json.Nodes.JsonNode? node, string path, int depth, List<InputField> sink)
    {
        if (node is System.Text.Json.Nodes.JsonObject obj)
        {
            foreach (var (key, child) in obj)
            {
                var childPath = path + (IsSimpleKey(key) ? "." + key : $"[\"{key}\"]");
                AddInputField(key, child, childPath, depth, sink);
            }
        }
        else if (node is System.Text.Json.Nodes.JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                AddInputField($"[{i}]", array[i], $"{path}[{i}]", depth, sink);
            }
        }
    }

    private static void AddInputField(string label, System.Text.Json.Nodes.JsonNode? child, string childPath, int depth, List<InputField> sink)
    {
        if (child is System.Text.Json.Nodes.JsonArray childArray)
        {
            sink.Add(new InputField(depth, label, childPath, $"[{childArray.Count}]"));
            FlattenInput(child, childPath, depth + 1, sink);
        }
        else if (child is System.Text.Json.Nodes.JsonObject)
        {
            sink.Add(new InputField(depth, label, childPath, "{…}"));
            FlattenInput(child, childPath, depth + 1, sink);
        }
        else
        {
            sink.Add(new InputField(depth, label, childPath, FieldPreview(child)));
        }
    }

    private static bool IsSimpleKey(string key) =>
        key.Length > 0 && (char.IsLetter(key[0]) || key[0] == '_') && key.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static string FieldPreview(System.Text.Json.Nodes.JsonNode? node)
    {
        var s = node?.ToString() ?? "null";
        return s.Length > 60 ? s[..60] + "…" : s;
    }

    // Secili kaynak node + alan path'inden ifade uretip son odaklanan girise ekler.
    private void InsertInputExpression(string path)
    {
        if (insertTarget is null || inputSelectedNodeId is null)
        {
            return;
        }

        var sourceName = nodes.FirstOrDefault(n => n.InstanceId == inputSelectedNodeId)?.Name;
        if (string.IsNullOrEmpty(sourceName))
        {
            return;
        }

        insertTarget($"{{{{$node[\"{sourceName}\"].json{path}}}}}");
    }

    // ---------- columnsJson icin kolon-bazli deger formu (db.insert/update/upsert) ----------
    private readonly Dictionary<string, string> columnValues = new();

    private static bool IsColumnMapNode(string nodeKey) =>
        nodeKey is "db.insert" or "db.update" or "db.upsert";

    // Mevcut columnsJson objesini kolon->deger sozlugune yukler (node acilisinda).
    private void LoadColumnValues(DesignerNode node)
    {
        columnValues.Clear();
        var raw = GetParam(node, "columnsJson");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(raw) is System.Text.Json.Nodes.JsonObject obj)
            {
                foreach (var (key, value) in obj)
                {
                    columnValues[key] = value?.ToString() ?? "";
                }
            }
        }
        catch
        {
            // Bozuk JSON: bos formla baslar.
        }
    }

    // Kolon->deger formunu columnsJson objesine geri yazar. Canli kolonlar biliniyorsa yalniz
    // onlari yazar (eski/silinmis kolonlarin bayat degerleri temizlenir); bilinmiyorsa hepsini korur.
    private void SyncColumnValues(DesignerNode node)
    {
        var live = ColumnMapKeys().Select(k => k.Name).ToHashSet();
        var obj = new System.Text.Json.Nodes.JsonObject();
        foreach (var (key, value) in columnValues)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrEmpty(value) && (live.Count == 0 || live.Contains(key)))
            {
                obj[key] = value;
            }
        }

        SetParam(node, "columnsJson", obj.ToJsonString());
    }

    // Render icin kolon listesi (ad + PK). Canli sema (tablodan yuklenen kolonlar) tek dogru kaynaktir;
    // yuklenememisse kaydedilmis deger anahtarlarina duser.
    private List<(string Name, bool IsPk)> ColumnMapKeys()
    {
        if (dynOptions.TryGetValue("columnsJson", out var opts) && opts.Count > 0)
        {
            return opts.Select(o => (o.Value, o.Label == "pk")).ToList();
        }

        return columnValues.Keys.Select(k => (k, false)).ToList();
    }

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
            var options = await Engine.LoadOptionsAsync(doc.RootElement, node.InstanceId, parameterKey, currentUserId, WorkflowId);
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

    private string? UpstreamOutput(DesignerNode node) => UpstreamPortJson(node);

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

            var text = result is JsonValue jv ? jv.ToString() : result.ToJsonString(FlowJson.Relaxed);
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
        // $json, bagli olunan KAYNAK PORTUN verisinden gelir (port-aware). Boylece Statuses dalindaki
        // bir node {{$json.status}}'u dogru onizler, port 0'daki (messages) veriyi degil.
        var portItems = ParseItems(UpstreamPortJson(node));
        if (portItems.Count > 0)
        {
            current = portItems[0];
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
        runPortOutputs.Clear();
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
                    StoreRunOutput(data);
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
            return first.ToJsonString(FlowJson.Relaxed);
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

    // Gosterim icin JSON: Unicode'u (Turkce dahil) escape etmeden, okunabilir yazar (ortak ayar).
    private static readonly JsonSerializerOptions DisplayJson = FlowJson.RelaxedIndented;

    private static RunOutput ToRunOutput(NodeRunData data) =>
        new(data.ItemCount,
            data.Output.ToJsonString(DisplayJson),
            data.Status == NodeRunStatus.Failed ? data.Error : null);
}
