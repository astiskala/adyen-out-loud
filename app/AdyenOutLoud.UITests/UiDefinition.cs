namespace AdyenOutLoud.UITests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiDefinition : ICollectionFixture<UiEnvironment>
{
    public const string Name = "iOS simulator";
}
