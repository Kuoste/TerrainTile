using Kuoste.LidarWorld.Tools.Logger;
using System.Threading;

namespace Kuoste.LidarWorld.Tile
{
    public class Builder : IBuilder
    {
        public CancellationToken CancellationToken { get; set; }
        public CompositeLogger Logger { get; set; }

        public bool IsCancellationRequested()
        {
            return CancellationToken != null && CancellationToken.IsCancellationRequested;
        }
    }
}
