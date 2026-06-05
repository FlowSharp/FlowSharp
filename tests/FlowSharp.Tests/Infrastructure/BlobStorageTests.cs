using System.Text.Json;
using FluentAssertions;
using FlowSharp.Application.Workflows;
using FlowSharp.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FlowSharp.Tests.Infrastructure;

public class BlobStorageTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), "fs-blob-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private FileSystemBlobStore NewStore() =>
        new(Options.Create(new BlobStorageOptions { Directory = tempDir }), NullLogger<FileSystemBlobStore>.Instance);

    [Fact]
    public async Task Save_then_get_returns_same_content()
    {
        var store = NewStore();
        var content = """{"hello":"dünya","n":42}""";

        var reference = await store.SaveAsync(content);
        var roundtrip = await store.GetAsync(reference);

        reference.Should().NotBeNullOrEmpty();
        roundtrip.Should().Be(content);
    }

    [Fact]
    public async Task Get_unknown_reference_returns_null()
    {
        var store = NewStore();
        (await store.GetAsync("nonexistent")).Should().BeNull();
    }

    [Fact]
    public async Task Delete_removes_content()
    {
        var store = NewStore();
        var reference = await store.SaveAsync("data");

        await store.DeleteAsync(reference);

        (await store.GetAsync(reference)).Should().BeNull();
    }

    [Fact]
    public void Marker_roundtrips_through_helper()
    {
        var marker = ExecutionOutputBlob.CreateMarker("abc123");

        ExecutionOutputBlob.TryGetReference(marker, out var reference).Should().BeTrue();
        reference.Should().Be("abc123");
    }

    [Fact]
    public void Regular_output_is_not_detected_as_marker()
    {
        var regular = JsonDocument.Parse("""{"result":[],"nodes":[]}""");

        ExecutionOutputBlob.TryGetReference(regular, out _).Should().BeFalse();
    }
}
