using System.Net.Http;
using FluentAssertions;
using FlowSharp.Application.Errors;
using Xunit;

namespace FlowSharp.Tests.Errors;

public class ErrorTranslatorTests
{
    private static ErrorTranslator NewTranslator() =>
        // Default'in built-in kurallariyla ayni davranisi izole test etmek icin yeni bir ornek degil,
        // dogrudan Default kullaniyoruz: built-in kurallar her zaman mevcuttur ve test bunlari mutate etmez.
        ErrorTranslator.Default;

    [Fact]
    public void Network_failure_gives_actionable_message_without_leaking_detail()
    {
        var error = NewTranslator().Translate(new HttpRequestException("Connection refused to 10.0.0.5:5432"));

        error.Category.Should().Be(ErrorCategory.Network);
        error.Message.Should().Contain("ulasilamadi");
        error.Message.Should().NotContain("10.0.0.5"); // ham teknik detay sizmamali
    }

    [Fact]
    public void Timeout_is_mapped_from_task_canceled()
    {
        var error = NewTranslator().Translate(new TaskCanceledException());

        error.Category.Should().Be(ErrorCategory.Timeout);
        error.Message.Should().Contain("zaman asimina");
    }

    [Fact]
    public void Aggregate_exception_is_unwrapped_to_inner_reason()
    {
        var error = NewTranslator().Translate(new AggregateException(new InvalidOperationException("Workflow bulunamadi")));

        error.Message.Should().Contain("Workflow bulunamadi");
    }

    [Fact]
    public void Overly_long_message_is_truncated()
    {
        var error = NewTranslator().Translate(new Exception(new string('x', 500)));

        error.Message.Should().EndWith("…");
        error.Message.Length.Should().BeLessThan(300);
    }

    [Fact]
    public void Custom_rule_added_at_runtime_takes_precedence()
    {
        // "Dinamik" yapinin ozu: yeni bir kural eklenince mevcut kod degismeden devreye girer.
        var translator = new ErrorTranslator([
            ErrorRule.For<InvalidOperationException>("genel", ErrorCategory.Unknown)
        ]);
        translator.AddRule(ErrorRule.For<InvalidOperationException>("ozel kural", ErrorCategory.Configuration));

        translator.Translate(new InvalidOperationException("x")).Message.Should().Be("ozel kural");
    }
}
