using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Application.Telegram.Progress;
using SsSport.Drive.Domain.Assets;

namespace SsSport.Drive.Application.UnitTests.Telegram;

/// <summary>
/// T-6b: the progress message follows the files too, so the updater runs (in the background, T-6c) on every asset
/// event that changes what the build lines show.
/// </summary>
public sealed class PlayerProgressUpdaterEventsTests
{
    /// <summary>The updater is a background handler of each asset event.</summary>
    /// <param name="eventType">The asset event.</param>
    [Theory]
    [InlineData(typeof(AssetAdded))]
    [InlineData(typeof(AssetProgressed))]
    [InlineData(typeof(AssetReady))]
    [InlineData(typeof(AssetFailed))]
    [InlineData(typeof(AssetReplaced))]
    [InlineData(typeof(AssetHidden))]
    public void Listens_to_asset_events(Type eventType)
    {
        typeof(PlayerProgressUpdater).GetInterfaces()
            .Should().Contain(typeof(IBackgroundEventHandler<>).MakeGenericType(eventType));
    }
}
