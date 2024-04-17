using System.Threading;

namespace Kuoste.LidarWorld.Tile
{
    public class Builder : IBuilder
    {
        protected CancellationToken CancellationToken { get; private set; }

        public void SetCancellationToken(CancellationToken token)
        {
            CancellationToken = token;
        }
    }
}
