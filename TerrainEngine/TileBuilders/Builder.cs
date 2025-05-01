using Kuoste.TerrainEngine.DebugTools.Logger;
using Kuoste.TerrainEngine.Interfaces.DebugTools;
using Kuoste.TerrainEngine.Interfaces.TileBuilders;
using System.Threading;

namespace Kuoste.TerrainEngine.TileBuilders
{
    public class Builder : IBuilder
    {
        public CancellationToken CancellationToken { get; set; }
        public ILogger Logger { get; set; }

        public bool IsCancellationRequested()
        {
            return CancellationToken != null && CancellationToken.IsCancellationRequested;
        }
    }
}
