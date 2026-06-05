using System.Collections.Generic;

namespace FlowSharp.Web.Components.Pages;

/// <summary>
/// Tasarimci canvas'indaki bir node'un tarayici tarafi gorunum modeli: konum, anlik durum ve
/// duzenlenen parametreler. Yurutme motorunun <c>EngineNode</c>'undan ve domain'in
/// <c>NodeDefinition</c>'indan ayridir; yalniz editor durumu icindir.
/// </summary>
internal sealed class DesignerNode
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
