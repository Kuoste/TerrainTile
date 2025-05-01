using Kuoste.TerrainEngine.Interfaces.DebugTools;
using System.Threading;

namespace Kuoste.TerrainEngine.Interfaces.TileBuilders
{
    public interface IBuilder
    {
        CancellationToken CancellationToken { get; set; }
        ILogger Logger { get; set; }

        bool IsCancellationRequested();
    }
}
