using Kuoste.TerrainEngine.Common.Interfaces;
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
