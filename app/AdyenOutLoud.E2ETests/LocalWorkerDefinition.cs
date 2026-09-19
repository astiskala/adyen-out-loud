using AdyenOutLoud.TestSupport;

namespace AdyenOutLoud.E2ETests;

[CollectionDefinition(Name)]
public sealed class LocalWorkerDefinition : ICollectionFixture<LocalWorker>
{
    public const string Name = "Local Worker";
}
