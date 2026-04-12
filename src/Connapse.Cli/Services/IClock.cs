namespace Connapse.Cli.Services;

public interface IClock
{
    DateTime UtcNow { get; }
}
