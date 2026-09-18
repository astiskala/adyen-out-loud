namespace AdyenOutLoud.Abstractions;

public interface IBackgroundExecutionService
{
    Task EnterForegroundAsync();
    Task EnterBackgroundAsync();
}
