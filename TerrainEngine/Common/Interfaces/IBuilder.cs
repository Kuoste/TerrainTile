using System.Threading;

namespace Kuoste.TerrainEngine.Common.Interfaces
{
    public interface IBuilder
    {
        CancellationToken CancellationToken { get; set; }
        ILogger Logger { get; set; }

        bool IsCancellationRequested();
    }
}
