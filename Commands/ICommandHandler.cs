namespace ConfluencePageExporter.Commands;

public interface ICommandHandler
{
    Task<int> ExecuteAsync(CancellationToken ct);
}
