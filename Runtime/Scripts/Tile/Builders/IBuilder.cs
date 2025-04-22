
using Kuoste.LidarWorld.Tools.Logger;
using System.Threading;

namespace Kuoste.LidarWorld.Tile
{
    public interface IBuilder
    {
        CancellationToken CancellationToken { get; set; }
        CompositeLogger Logger { get; set; }

        bool IsCancellationRequested();
    }
}
