using VetSystem.Domain.Common;

namespace VetSystem.API.Endpoints.Sync;

public interface ISyncDispatcher
{
    ISyncTableHandler Resolve(string table);
}

public sealed class SyncDispatcher : ISyncDispatcher
{
    private readonly IReadOnlyDictionary<string, ISyncTableHandler> _handlers;

    public SyncDispatcher(IEnumerable<ISyncTableHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.Table, StringComparer.OrdinalIgnoreCase);
    }

    public ISyncTableHandler Resolve(string table)
    {
        if (_handlers.TryGetValue(table, out var handler))
        {
            return handler;
        }

        throw new NotFoundException("sync_table", table);
    }
}
