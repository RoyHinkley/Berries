using Berries.Core.Analysis;
using Xunit;

namespace Berries.Core.Tests;

public sealed class AnalysisProductTests
{
    [Fact]
    public void StaleComputationCannotReplaceLastCompletedResult()
    {
        var product = new AnalysisProduct<Result>();

        Assert.True(product.TryBegin(1, CancellationToken.None, out _));
        Assert.True(product.TryPublish(1, 1, new Result("one")));
        Assert.True(product.IsValid(1));

        Assert.True(product.TryBegin(2, CancellationToken.None, out _));
        Assert.False(product.IsValid(2));
        Assert.Equal("one", product.Result!.Value);
        Assert.Equal(1, product.ResultGeneration);

        Assert.False(product.TryPublish(2, 3, new Result("two")));
        Assert.Equal("one", product.Result!.Value);
        Assert.Equal(1, product.ResultGeneration);
    }

    [Fact]
    public void ObsoleteRunIsCancelledWithoutDiscardingCompletedResult()
    {
        var product = new AnalysisProduct<Result>();
        Assert.True(product.TryBegin(1, CancellationToken.None, out _));
        Assert.True(product.TryPublish(1, 1, new Result("one")));

        Assert.True(product.TryBegin(2, CancellationToken.None, out var runningToken));
        product.CancelObsolete(3);

        Assert.True(runningToken.IsCancellationRequested);
        Assert.Equal("one", product.Result!.Value);
        Assert.Equal(1, product.ResultGeneration);
    }

    private sealed record Result(string Value);
}
