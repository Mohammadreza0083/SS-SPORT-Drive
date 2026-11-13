using SsSport.Drive.Application.Telegram.Progress;
using SsSport.Drive.Domain.Assets;

namespace SsSport.Drive.Application.UnitTests.Telegram;

/// <summary>
/// T-6b: the build of a player's files, worked out from where each file is in its pipeline. Each file weighs the same;
/// within a file: queued 0, copying 0.1, processing 0.3 (videos: 0.3 + 0.7 × ready renditions / planned renditions),
/// ready or failed 1. The percentage is the average, rounded down.
/// </summary>
public sealed class BuildProgressTests
{
    /// <summary>A fixed time.</summary>
    private static readonly DateTimeOffset _now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A player without files has nothing to build and nothing finished.</summary>
    [Fact]
    public void No_files_is_none()
    {
        var build = BuildProgress.From([]);

        build.Should().Be(BuildProgress.None);
        build.IsBuilding.Should().BeFalse();
        build.IsFinished.Should().BeFalse();
    }

    /// <summary>The weight of one file at each step.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="state">The state.</param>
    /// <param name="planned">Planned renditions.</param>
    /// <param name="ready">Ready renditions.</param>
    /// <param name="percent">The expected percentage.</param>
    [Theory]
    [InlineData(AssetKind.Video, ProcessingState.Queued, 0, 0, 0)]
    [InlineData(AssetKind.Document, ProcessingState.Copying, 0, 0, 10)]
    [InlineData(AssetKind.Video, ProcessingState.Processing, 0, 0, 30)]
    [InlineData(AssetKind.Video, ProcessingState.Processing, 3, 1, 53)]
    [InlineData(AssetKind.Video, ProcessingState.Processing, 2, 1, 65)]
    [InlineData(AssetKind.Scout, ProcessingState.Processing, 0, 0, 30)]
    [InlineData(AssetKind.Document, ProcessingState.Ready, 0, 0, 100)]
    [InlineData(AssetKind.Video, ProcessingState.Failed, 3, 1, 100)]
    public void One_file_weighs_by_its_step(AssetKind kind, ProcessingState state, int planned, int ready, int percent)
    {
        BuildProgress.From([new AssetBuildRow(kind, state, planned, ready)]).Percent.Should().Be(percent);
    }

    /// <summary>A mix of files: every count behind the message lines, and the average percentage.</summary>
    [Fact]
    public void Counts_a_mix_of_files()
    {
        var build = BuildProgress.From(
        [
            new AssetBuildRow(AssetKind.Video, ProcessingState.Ready, 3, 3),
            new AssetBuildRow(AssetKind.Video, ProcessingState.Processing, 3, 1),
            new AssetBuildRow(AssetKind.Document, ProcessingState.Ready, 0, 0),
            new AssetBuildRow(AssetKind.Scout, ProcessingState.Processing, 0, 0),
            new AssetBuildRow(AssetKind.Document, ProcessingState.Failed, 0, 0),
            new AssetBuildRow(AssetKind.Video, ProcessingState.Queued, 0, 0),
        ]);

        build.Should().Be(new BuildProgress(
            Files: 6,
            Copied: 4,
            CopyDone: false,
            Videos: 3,
            VideosReady: 1,
            VideosDone: false,
            Documents: 3,
            DocumentsReady: 1,
            DocumentsDone: false,
            Failed: 1,
            Percent: 63));
        build.IsBuilding.Should().BeTrue();
        build.IsFinished.Should().BeFalse();
    }

    /// <summary>When every file is ready or failed the build is finished at 100 %.</summary>
    [Fact]
    public void Ready_and_failed_files_finish_the_build()
    {
        var build = BuildProgress.From(
        [
            new AssetBuildRow(AssetKind.Video, ProcessingState.Ready, 2, 2),
            new AssetBuildRow(AssetKind.Document, ProcessingState.Failed, 0, 0),
        ]);

        build.Percent.Should().Be(100);
        build.CopyDone.Should().BeTrue();
        build.VideosDone.Should().BeTrue();
        build.DocumentsDone.Should().BeTrue();
        build.Copied.Should().Be(1, "the failed file never reached storage");
        build.IsBuilding.Should().BeFalse();
        build.IsFinished.Should().BeTrue();
    }

    /// <summary>The time left grows the time spent by what is still to do: 50 % in 6 minutes leaves 6 minutes.</summary>
    [Fact]
    public void Estimates_the_time_left_from_the_time_spent()
    {
        var build = BuildProgress.From(
        [
            new AssetBuildRow(AssetKind.Document, ProcessingState.Ready, 0, 0),
            new AssetBuildRow(AssetKind.Document, ProcessingState.Queued, 0, 0),
        ]);

        build.EstimateRemaining(_now.AddMinutes(-6), _now).Should().Be(TimeSpan.FromMinutes(6));
    }

    /// <summary>25 % in 10 minutes leaves 30 minutes.</summary>
    [Fact]
    public void Estimates_with_the_rounded_percentage()
    {
        var build = new BuildProgress(4, 1, false, 0, 0, true, 4, 1, false, 0, 25);

        build.EstimateRemaining(_now.AddMinutes(-10), _now).Should().Be(TimeSpan.FromMinutes(30));
    }

    /// <summary>No estimate at 0 %, without a start, or once the build is finished.</summary>
    [Fact]
    public void No_estimate_when_it_cannot_be_made()
    {
        var started = BuildProgress.From([new AssetBuildRow(AssetKind.Video, ProcessingState.Queued, 0, 0)]);
        var halfway = BuildProgress.From(
        [
            new AssetBuildRow(AssetKind.Document, ProcessingState.Ready, 0, 0),
            new AssetBuildRow(AssetKind.Document, ProcessingState.Queued, 0, 0),
        ]);
        var finished = BuildProgress.From([new AssetBuildRow(AssetKind.Document, ProcessingState.Ready, 0, 0)]);

        started.EstimateRemaining(_now.AddMinutes(-5), _now).Should().BeNull("nothing is done yet");
        halfway.EstimateRemaining(null, _now).Should().BeNull("the start is unknown");
        finished.EstimateRemaining(_now.AddMinutes(-5), _now).Should().BeNull("nothing is left");
    }
}
