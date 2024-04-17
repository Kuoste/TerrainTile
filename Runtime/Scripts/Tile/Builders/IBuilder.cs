
using System.Threading;

namespace Kuoste.LidarWorld.Tile
{
    public interface IBuilder
    {
        public void SetCancellationToken(CancellationToken token);
    }
}
