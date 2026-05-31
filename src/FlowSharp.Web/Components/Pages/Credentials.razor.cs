using Microsoft.AspNetCore.Components;
using FlowSharp.Application.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FlowSharp.Web.Components.Pages;

public partial class Credentials
{
    [Inject] public ICredentialStore CredentialStore { get; set; } = default!;

    private IReadOnlyList<CredentialSummary>? items;
    private bool editing;
    private string? message;
    private CredentialForm form = new();
    private readonly List<FieldRow> fields = [];

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    private async Task ReloadAsync() => items = await CredentialStore.ListAsync();

    private void StartCreate()
    {
        form = new CredentialForm();
        fields.Clear();
        fields.Add(new FieldRow { Key = "apiKey" });
        editing = true;
        message = null;
    }

    private async Task EditAsync(Guid id)
    {
        var detail = await CredentialStore.GetAsync(id);
        if (detail is null) return;
        form = new CredentialForm { Id = detail.Id, Name = detail.Name, Type = detail.Type };
        fields.Clear();
        foreach (var pair in detail.Data)
        {
            fields.Add(new FieldRow { Key = pair.Key, Value = pair.Value });
        }
        editing = true;
        message = null;
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(form.Name) || string.IsNullOrWhiteSpace(form.Type))
        {
            message = "Ad ve tip zorunlu.";
            return;
        }
        var data = fields.Where(f => !string.IsNullOrWhiteSpace(f.Key))
            .ToDictionary(f => f.Key, f => f.Value ?? string.Empty);
        await CredentialStore.SaveAsync(new CredentialInput(form.Id, form.Name, form.Type, data));
        editing = false;
        await ReloadAsync();
    }

    private async Task DeleteAsync(Guid id)
    {
        await CredentialStore.DeleteAsync(id);
        await ReloadAsync();
    }

    private void Cancel() => editing = false;

    private sealed class CredentialForm
    {
        public Guid? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }

    private sealed class FieldRow
    {
        public string Key { get; set; } = string.Empty;
        public string? Value { get; set; }
    }
}
